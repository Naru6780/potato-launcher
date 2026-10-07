namespace PotatoLauncher;

/// <summary>How clients are placed on CPU cores.</summary>
internal enum CpuPlacementMode
{
    /// <summary>No pinning: Windows schedules every client on every core.</summary>
    Off,
    /// <summary>The main client gets fast cores of its own (on the 3D V-Cache CCD); each other client stays on one CCD.</summary>
    ReserveMain,
    /// <summary>Like ReserveMain, but followers are packed into two-core lanes instead of a whole CCD.</summary>
    Lanes,
    /// <summary>
    /// Two-CCD chips only: the main gets the whole 3D V-Cache CCD so no follower shares its cache, and followers use the
    /// other CCD(s) while they fit there. Falls back to ReserveMain when they do not.
    /// </summary>
    CacheCcdForMain
}

/// <summary>The user's choice. Auto uses the measured winner of "Test placements", or a topology-based default.</summary>
internal enum CpuPlacementSetting
{
    Auto,
    Off,
    ReserveMain,
    Lanes,
    CacheCcdForMain
}

/// <summary>What the last plan chose, fed back into the next one for hysteresis.</summary>
internal sealed record PlacementState(int? MainCores, bool CacheCcdEngaged);

internal static class CpuPlacementPlanner
{
    /// <summary>
    /// Default before a test on this PC. Measured on a 9800X3D with 8 clients: giving the main its own cores cut total
    /// CPU from 43.7% to 40.0% at the same FPS, so it is the default wherever the core layout can be read.
    /// </summary>
    public static CpuPlacementMode DefaultFor(CpuTopology topology) =>
        topology.IsSupported ? CpuPlacementMode.ReserveMain : CpuPlacementMode.Off;

    // Followers keep this much room over their measured load before the main takes another core. Their load is bounded
    // by their frame cap, so a small margin is enough; 1.5x (up to 1.0.129) left a 9950X3D's main 2 cores with 15
    // followers at 20 threads, and it sat at 41-45 FPS while 8 of the 32 threads idled.
    internal const double FollowerHeadroom = 1.15;
    // A core the main already holds is given up only once the followers would drop below this margin (hysteresis).
    internal const double FollowerShrinkHeadroom = 1.05;
    // Assumed per-follower load (logical CPUs) before Potato has measured any.
    internal const double UnmeasuredFollowerLoad = 1.0;

    // A reservation smaller than this starves the main instead of protecting it (seen on a 9950X3D in 1.0.126: followers
    // got busier, the main was squeezed to one core = 2 threads and fell from 60 to 35 FPS). Sharing every core instead
    // measured worse still ("No pinning": 36 FPS on the same PC), so the main never goes below this.
    internal const int MinimumMainCores = 2;
    // The main's own measured load, with headroom, must fit in its reservation too.
    internal const double MainHeadroom = 1.5;

    /// <summary>
    /// Physical cores kept for the main client, the main first: up to half of the fastest CCD (at most 4), reduced
    /// only while the followers would otherwise drop below their measured load plus <see cref="FollowerHeadroom"/>,
    /// never below <see cref="MinimumMainCores"/> or what the main's own load needs. <paramref name="previous"/> (the
    /// count held now) is kept while the followers still have <see cref="FollowerShrinkHeadroom"/>.
    /// </summary>
    public static int MainCoreCount(CpuTopology topology, int followers, double? followerLoad, double? mainLoad = null, int? previous = null)
    {
        if (topology.Domains.Count == 0) return 0;
        var load = followerLoad ?? followers * UnmeasuredFollowerLoad;
        var first = topology.Domains[0].Cores;
        var threadsPerCore = Math.Max(1, System.Numerics.BitOperations.PopCount(unchecked((ulong)first[0].Mask)));
        var largest = Math.Clamp(first.Count / 2, 1, 4);
        var needed = Math.Min(largest, Math.Max(MinimumMainCores, (int)Math.Ceiling((mainLoad ?? 0) * MainHeadroom / threadsPerCore)));
        var count = Math.Max(needed, MaxMainCores(topology, load * FollowerHeadroom));
        if (previous is int held && held > count && held <= largest)
            count = Math.Max(count, Math.Min(held, MaxMainCores(topology, load * FollowerShrinkHeadroom)));
        return count;
    }

    /// <summary>
    /// Placements worth measuring on this PC. "Main gets the cache CCD" is left out when the followers do not fit in
    /// the other CCD(s): it would silently fall back to "Main gets its own cores" and win or lose by noise (seen on a
    /// 9950X3D with 15 followers at 20 threads: identical placements, 43 vs 44 FPS).
    /// </summary>
    public static List<CpuPlacementMode> TestCandidates(CpuTopology topology, int followers, double? followerLoad)
    {
        List<CpuPlacementMode> modes = [CpuPlacementMode.Off, CpuPlacementMode.ReserveMain, CpuPlacementMode.Lanes];
        if (FollowersFitOutsideCacheCcd(topology, followers, followerLoad, CacheCcdReenterHeadroom)) modes.Add(CpuPlacementMode.CacheCcdForMain);
        return modes;
    }

    // Cores only come back to the main once a bigger reservation has stayed possible this long. Giving cores up is
    // immediate. (1.0.126 used a stricter regrow threshold instead, which could leave the main stuck on one core.)
    internal static readonly TimeSpan RegrowAfter = TimeSpan.FromSeconds(60);

    /// <summary>Applies the regrow delay to a target core count. <paramref name="regrowSince"/> is caller state.</summary>
    public static int WithRegrowDelay(int target, int? previous, DateTime nowUtc, ref DateTime? regrowSince)
    {
        if (previous is not int current || target <= current)
        {
            regrowSince = null;
            return target;
        }
        regrowSince ??= nowUtc;
        return nowUtc - regrowSince.Value >= RegrowAfter ? target : current;
    }

    private static int MaxMainCores(CpuTopology topology, double neededForFollowers)
    {
        if (topology.Domains.Count == 0) return 0;
        var first = topology.Domains[0].Cores;
        for (var count = Math.Clamp(first.Count / 2, 1, 4); count > 0; count--)
        {
            var reserved = first.Take(count).Sum(core => System.Numerics.BitOperations.PopCount(unchecked((ulong)core.Mask)));
            if (topology.LogicalCount - reserved >= neededForFollowers) return count;
        }
        return 0;
    }

    /// <summary>
    /// Affinity mask per client. Clients are listed oldest first so the plan stays stable as clients come and go.
    /// A follower never straddles two CCDs: crossing them costs a game far more than sharing cores does.
    /// </summary>
    public static Dictionary<int, long> Plan(CpuTopology topology, CpuPlacementMode mode, IReadOnlyList<int> clientIds, int? mainId, double? followerLoad = null) =>
        Plan(topology, mode, clientIds, mainId, followerLoad, previous: null, out _);

    /// <summary>
    /// Plan with memory of the previous decision (hysteresis), so a follower load near a threshold cannot make the
    /// placement flip back and forth. Pass the returned state back in on the next call.
    /// </summary>
    public static Dictionary<int, long> Plan(CpuTopology topology, CpuPlacementMode mode, IReadOnlyList<int> clientIds, int? mainId,
        double? followerLoad, PlacementState? previous, out PlacementState used, double? mainLoad = null, int? mainCores = null)
    {
        used = new PlacementState(null, false);
        var plan = new Dictionary<int, long>();
        if (clientIds.Count == 0) return plan;
        if (mode == CpuPlacementMode.Off || !topology.IsSupported || clientIds.Count == 1)
        {
            foreach (var id in clientIds) plan[id] = topology.AllMask;
            return plan;
        }

        // Cores left for followers, per cache domain.
        var remaining = topology.Domains.Select(domain => domain.Cores.ToList()).ToList();
        var hasMain = mainId is int main && clientIds.Contains(main);
        if (mode == CpuPlacementMode.CacheCcdForMain)
        {
            var fits = previous?.CacheCcdEngaged == true
                ? FollowersFitOutsideCacheCcd(topology, clientIds.Count - 1, followerLoad)
                : FollowersFitOutsideCacheCcd(topology, clientIds.Count - 1, followerLoad, CacheCcdReenterHeadroom);
            if (hasMain && fits)
            {
                used = new PlacementState(null, true);
                plan[mainId!.Value] = topology.Domains[0].Mask;
                var others = topology.Domains.Skip(1).Select(domain => domain.Mask).ToList();
                var load = new int[others.Count];
                foreach (var id in clientIds.Where(id => id != mainId))
                {
                    var best = 0;
                    for (var index = 1; index < others.Count; index++)
                        if ((load[index] + 1d) / Threads(others[index]) < (load[best] + 1d) / Threads(others[best])) best = index;
                    plan[id] = others[best];
                    load[best]++;
                }
                return plan;
            }
            mode = CpuPlacementMode.ReserveMain;
        }
        if (hasMain)
        {
            var count = mainCores ?? MainCoreCount(topology, clientIds.Count - 1, followerLoad, mainLoad, previous?.MainCores);
            used = new PlacementState(count, false);
            var reserved = remaining[0].Take(count).ToList();
            // Never leave followers with nothing.
            if (reserved.Count > 0 && remaining.Sum(cores => cores.Count) - reserved.Count >= 1)
            {
                plan[mainId!.Value] = reserved.Aggregate(0L, (mask, core) => mask | core.Mask);
                remaining[0].RemoveRange(0, reserved.Count);
            }
            else
            {
                // Cannot happen with a readable layout (the main gets at most half a CCD); kept so the main is never
                // dealt a follower pool or lane: it shares the whole fastest CCD.
                plan[mainId!.Value] = topology.Domains[0].Mask;
            }
        }

        var pools = new List<long>();
        foreach (var cores in remaining.Where(cores => cores.Count > 0))
        {
            if (mode == CpuPlacementMode.ReserveMain)
            {
                pools.Add(cores.Aggregate(0L, (mask, core) => mask | core.Mask));
                continue;
            }
            // Lanes of two physical cores; a leftover single core joins the previous lane of the same CCD.
            var lanes = cores.Chunk(2).Select(chunk => chunk.Aggregate(0L, (mask, core) => mask | core.Mask)).ToList();
            if (lanes.Count > 1 && cores.Count % 2 == 1)
            {
                lanes[^2] |= lanes[^1];
                lanes.RemoveAt(lanes.Count - 1);
            }
            pools.AddRange(lanes);
        }
        if (pools.Count == 0) pools.Add(topology.AllMask);

        // Fill pools in proportion to their size: the next follower goes where it adds the least load per thread.
        var assigned = new int[pools.Count];
        foreach (var id in clientIds.Where(id => !plan.ContainsKey(id)))
        {
            var best = 0;
            for (var index = 1; index < pools.Count; index++)
            {
                if ((assigned[index] + 1d) / Threads(pools[index]) < (assigned[best] + 1d) / Threads(pools[best])) best = index;
            }
            plan[id] = pools[best];
            assigned[best]++;
        }
        return plan;
    }

    // The followers' measured load must fit in the non-cache CCD(s) with 25% to spare. Re-checked every second, so a
    // crowded area moves them back to ReserveMain on its own. Looser than the 1.5x used to size the main's share,
    // because Test placements verifies FPS before Auto ever picks this mode.
    internal const double CacheCcdHeadroom = 1.25;

    // Moving followers back off the cache CCD once they left it needs more room than staying there.
    internal const double CacheCcdReenterHeadroom = 1.5;

    internal static bool FollowersFitOutsideCacheCcd(CpuTopology topology, int followers, double? followerLoad, double headroom = CacheCcdHeadroom)
    {
        if (topology.Domains.Count < 2 || followers == 0) return false;
        var outside = topology.Domains.Skip(1).Sum(domain => domain.LogicalCount);
        return (followerLoad ?? followers * UnmeasuredFollowerLoad) * headroom <= outside;
    }

    private static int Threads(long mask) => Math.Max(1, System.Numerics.BitOperations.PopCount(unchecked((ulong)mask)));
}
