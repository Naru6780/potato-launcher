namespace PotatoLauncher;

/// <summary>How clients are placed on CPU cores.</summary>
internal enum CpuPlacementMode
{
    /// <summary>No pinning: Windows schedules every client on every core.</summary>
    Off,
    /// <summary>The main client gets fast cores of its own (on the 3D V-Cache CCD); each other client stays on one CCD.</summary>
    ReserveMain,
    /// <summary>Like ReserveMain, but followers are packed into two-core lanes instead of a whole CCD.</summary>
    Lanes
}

/// <summary>The user's choice. Auto uses the measured winner of "Test placements", or a topology-based default.</summary>
internal enum CpuPlacementSetting
{
    Auto,
    Off,
    ReserveMain,
    Lanes
}

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
    public static int MainCoreCount(CpuTopology topology, int followers, double? followerLoad)
    {
        if (topology.Domains.Count == 0) return 0;
        var first = topology.Domains[0].Cores;
        var needed = (followerLoad ?? followers * UnmeasuredFollowerLoad) * FollowerHeadroom;
        for (var count = Math.Clamp(first.Count / 2, 1, 4); count > 0; count--)
        {
            var reserved = first.Take(count).Sum(core => System.Numerics.BitOperations.PopCount(unchecked((ulong)core.Mask)));
            if (topology.LogicalCount - reserved >= needed) return count;
        }
        return 0;
    }

    /// <summary>
    /// Affinity mask per client. Clients are listed oldest first so the plan stays stable as clients come and go.
    /// A follower never straddles two CCDs: crossing them costs a game far more than sharing cores does.
    /// </summary>
    public static Dictionary<int, long> Plan(CpuTopology topology, CpuPlacementMode mode, IReadOnlyList<int> clientIds, int? mainId, double? followerLoad = null)
    {
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
        if (hasMain)
        {
            var reserved = remaining[0].Take(MainCoreCount(topology, clientIds.Count - 1, followerLoad)).ToList();
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

    private static int Threads(long mask) => Math.Max(1, System.Numerics.BitOperations.PopCount(unchecked((ulong)mask)));
}
