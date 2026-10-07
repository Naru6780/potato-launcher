using System.Numerics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

/// <summary>One physical core: its logical-processor mask and Windows' efficiency class (higher = faster core).</summary>
internal sealed record CpuCore(long Mask, int EfficiencyClass);

/// <summary>Cores that share one last-level cache (an AMD CCD, or the whole chip on most Intel CPUs).</summary>
internal sealed record CacheDomain(long Mask, long CacheBytes, IReadOnlyList<CpuCore> Cores)
{
    public int LogicalCount => BitOperations.PopCount(unchecked((ulong)Mask));
}

/// <summary>
/// Last-level-cache layout of this PC. Placement only makes a difference when cores are unequal: two CCDs (one of them
/// with 3D V-Cache), or Intel P/E cores. On a single-CCD chip such as the 9800X3D every core is the same.
/// </summary>
internal sealed class CpuTopology
{
    public IReadOnlyList<CacheDomain> Domains { get; }
    public int LogicalCount { get; }
    public bool IsSupported { get; }

    public CpuTopology(IReadOnlyList<CacheDomain> domains, int logicalCount, bool isSupported = true)
    {
        // Largest cache first (the V-Cache CCD), then lowest CPU number; fastest cores first inside a domain.
        Domains = domains
            .Where(domain => domain.Cores.Count > 0)
            .OrderByDescending(domain => domain.CacheBytes)
            .ThenBy(domain => BitOperations.TrailingZeroCount(domain.Mask))
            .Select(domain => domain with
            {
                Cores = domain.Cores.OrderByDescending(core => core.EfficiencyClass).ThenBy(core => BitOperations.TrailingZeroCount(core.Mask)).ToList()
            })
            .ToList();
        LogicalCount = logicalCount;
        IsSupported = isSupported && Domains.Count > 0 && logicalCount <= 64;
    }

    public long AllMask => LogicalCount >= 64 ? -1L : (1L << LogicalCount) - 1;

    /// <summary>True when cores differ (several caches, or P and E cores), which is when pinning can help.</summary>
    public bool HasUnequalCores =>
        Domains.Count > 1 || Domains.SelectMany(domain => domain.Cores).Select(core => core.EfficiencyClass).Distinct().Count() > 1;

    /// <summary>Stable text used to tell whether a stored placement test was made on this same CPU.</summary>
    public string Signature => string.Join("|", Domains.Select(domain => $"{domain.Mask:X}:{domain.CacheBytes / (1024 * 1024)}"));

    public string Describe()
    {
        if (!IsSupported) return $"{LogicalCount} logical CPUs (layout not readable)";
        if (Domains.Count == 1)
        {
            var domain = Domains[0];
            return $"1 CCD, {domain.Cores.Count} cores / {domain.LogicalCount} threads, {domain.CacheBytes / (1024 * 1024)} MB L3: every core is equal";
        }
        return $"{Domains.Count} CCDs: " + string.Join(", ", Domains.Select(domain =>
            $"{domain.CacheBytes / (1024 * 1024)} MB L3 on CPUs {FormatMask(domain.Mask)}"));
    }

    /// <summary>"0-7,16-23" style text; "all" for the full mask.</summary>
    public static string FormatMask(long mask, long allMask = 0)
    {
        if (mask == 0) return "none";
        if (allMask != 0 && mask == allMask) return "all";
        var ranges = new List<string>();
        var bit = 0;
        while (bit < 64)
        {
            if ((mask & (1L << bit)) == 0) { bit++; continue; }
            var start = bit;
            while (bit + 1 < 64 && (mask & (1L << (bit + 1))) != 0) bit++;
            ranges.Add(start == bit ? $"{start}" : $"{start}-{bit}");
            bit++;
        }
        return string.Join(",", ranges);
    }

    private static CpuTopology? current;
    public static CpuTopology Current => current ??= Detect();

    internal static CpuTopology Detect()
    {
        var logical = Environment.ProcessorCount;
        try
        {
            var cores = new List<CpuCore>();
            var caches = new List<(long Mask, long Bytes, int Level)>();
            var otherGroups = false;
            foreach (var (relationship, entry) in ReadEntries())
            {
                if (relationship == RelationProcessorCore)
                {
                    var efficiency = Marshal.ReadByte(entry, 9);
                    var groupCount = (ushort)Marshal.ReadInt16(entry, 30);
                    var mask = Marshal.ReadInt64(entry, 32);
                    var group = (ushort)Marshal.ReadInt16(entry, 40);
                    if (groupCount != 1 || group != 0) { otherGroups = true; continue; }
                    if (mask != 0) cores.Add(new CpuCore(mask, efficiency));
                }
                else if (relationship == RelationCache)
                {
                    var level = Marshal.ReadByte(entry, 8);
                    var bytes = (uint)Marshal.ReadInt32(entry, 12);
                    var type = Marshal.ReadInt32(entry, 16);
                    var mask = Marshal.ReadInt64(entry, 40);
                    var group = (ushort)Marshal.ReadInt16(entry, 48);
                    if (group != 0) { otherGroups = true; continue; }
                    if (type is CacheUnified or CacheData && mask != 0) caches.Add((mask, bytes, level));
                }
            }
            return FromParts(cores, caches, logical, supported: !otherGroups);
        }
        catch
        {
            return new CpuTopology([], logical, isSupported: false);
        }
    }

    /// <summary>Builds the topology from raw core and cache entries; the last cache level present defines the domains.</summary>
    internal static CpuTopology FromParts(IReadOnlyList<CpuCore> cores, IReadOnlyList<(long Mask, long Bytes, int Level)> caches, int logicalCount, bool supported = true)
    {
        if (cores.Count == 0 || caches.Count == 0) return new CpuTopology([], logicalCount, isSupported: false);
        var lastLevel = caches.Max(cache => cache.Level);
        var domains = caches
            .Where(cache => cache.Level == lastLevel)
            .GroupBy(cache => cache.Mask)
            .Select(group => new CacheDomain(group.Key, group.Max(cache => cache.Bytes),
                cores.Where(core => (core.Mask & group.Key) == core.Mask).ToList()))
            .ToList();
        // Any core not covered by a last-level cache entry still has to be placeable.
        var covered = domains.Aggregate(0L, (mask, domain) => mask | domain.Mask);
        var orphans = cores.Where(core => (core.Mask & covered) == 0).ToList();
        if (orphans.Count > 0)
            domains.Add(new CacheDomain(orphans.Aggregate(0L, (mask, core) => mask | core.Mask), 0, orphans));
        return new CpuTopology(domains, logicalCount, supported);
    }

    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;
    private const int RelationAll = 0xFFFF;
    private const int CacheUnified = 0;
    private const int CacheData = 2;

    private static IEnumerable<(int Relationship, IntPtr Entry)> ReadEntries()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref length);
        if (length == 0) yield break;
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationAll, buffer, ref length)) yield break;
            var offset = 0;
            while (offset + 8 <= length)
            {
                var entry = IntPtr.Add(buffer, offset);
                var relationship = Marshal.ReadInt32(entry, 0);
                var size = Marshal.ReadInt32(entry, 4);
                if (size <= 0) yield break;
                yield return (relationship, entry);
                offset += size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);
}
