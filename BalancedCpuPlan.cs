namespace PotatoLauncher;

internal static class BalancedCpuPlan
{
    // Share a complete cache domain so a rendering thread can use idle capacity.
    // Main roles deliberately receive no exclusive reservation in this throughput mode.
    internal static IReadOnlyDictionary<int, long> Create(IReadOnlyList<int> clientIds,
        IReadOnlyList<long> cores, IReadOnlyList<ProcessorCacheDomain> domains, bool hybridOrUnknown = false)
    {
        var full = cores.Aggregate(0L, (mask, core) => mask | core);
        if (full == 0) throw new InvalidOperationException("CPU topology is unavailable.");
        var pools = domains.Select(domain => domain.Mask & full).Where(mask => mask != 0).Distinct().ToList();
        var covered = pools.Aggregate(0L, (mask, pool) => mask | pool);
        // Incomplete or overlapping domains cannot provide reliable independent pools.
        if (covered != full || pools.Where((pool, index) => pools.Take(index).Any(other => (other & pool) != 0)).Any())
            pools = [full];
        // Unequal caches/SMT widths may indicate hybrid cores or asymmetric X3D dies.
        // Do not infer equal performance from logical processor counts in that case.
        // Small pools and lightly loaded PCs benefit from retaining scheduler freedom.
        if (hybridOrUnknown || cores.Select(ProcessorAffinity.CountSetBits).Distinct().Count() > 1 ||
            domains.Select(domain => domain.CacheSizeBytes).Distinct().Count() > 1 ||
            pools.Any(pool => ProcessorAffinity.CountSetBits(pool) < 4 || cores.Any(core => (core & pool) != 0 && (core & pool) != core)) ||
            clientIds.Count < pools.Count * 2)
            pools = [full];
        var loads = new int[pools.Count];
        var result = new Dictionary<int, long>();
        foreach (var id in clientIds)
        {
            var best = Enumerable.Range(0, pools.Count)
                .OrderBy(index => loads[index] / (double)ProcessorAffinity.CountSetBits(pools[index]))
                .ThenBy(index => index).First();
            result[id] = pools[best];
            loads[best]++;
        }
        return result;
    }
}
