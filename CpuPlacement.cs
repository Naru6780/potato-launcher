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

    // Followers keep at least this much room over what they actually use, so a busy moment never starves them.
    internal const double FollowerHeadroom = 1.5;
    // Assumed per-follower load (logical CPUs) before Potato has measured any.
    internal const double UnmeasuredFollowerLoad = 1.0;

    /// <summary>
    /// Physical cores kept for the main client: up to half of the fastest CCD (at most 4), but never so many that the
    /// followers' cores drop below their measured load plus headroom. 0 = the main shares cores with everyone.
    /// </summary>
    public static int MainCoreCount(CpuTopology topology, int followers, double? followerLoad) =>
        MaxMainCores(topology, (followerLoad ?? followers * UnmeasuredFollowerLoad) * FollowerHeadroom);

    // Taking cores back for the main needs this much room: with one threshold, a follower load sitting on it made the
    // reservation flip every few seconds and re-pin every client each time (seen in 1.0.125: 0-7 <-> 0-5).
    internal const double RegrowHeadroom = 1.8;

    /// <summary>
    /// Same as MainCoreCount, with hysteresis: the main gives cores up as soon as followers need them, but only takes
    /// them back once followers clearly have room (RegrowHeadroom), so a load near the threshold cannot flip it.
    /// </summary>
    public static int MainCoreCount(CpuTopology topology, int followers, double? followerLoad, int? previous)
    {
        var load = followerLoad ?? followers * UnmeasuredFollowerLoad;
        var allowed = MaxMainCores(topology, load * FollowerHeadroom);
        if (previous is not int current || current > allowed) return allowed;
        return Math.Min(allowed, Math.Max(current, MaxMainCores(topology, load * RegrowHeadroom)));
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
        double? followerLoad, PlacementState? previous, out PlacementState used)
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
            var count = MainCoreCount(topology, clientIds.Count - 1, followerLoad, previous?.MainCores);
            used = new PlacementState(count, false);
            var reserved = remaining[0].Take(count).ToList();
            // Never leave followers with nothing.
            if (reserved.Count > 0 && remaining.Sum(cores => cores.Count) - reserved.Count >= 1)
            {
                plan[mainId!.Value] = reserved.Aggregate(0L, (mask, core) => mask | core.Mask);
                remaining[0].RemoveRange(0, reserved.Count);
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
