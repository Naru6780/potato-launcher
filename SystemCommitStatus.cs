using System.Runtime.InteropServices;

namespace PotatoLauncher;

internal readonly record struct SystemCommitStatus(ulong UsedBytes, ulong LimitBytes)
{
    public bool IsCritical => LimitBytes > 0 && UsedBytes / (double)LimitBytes >= .95;
    public string Summary => LimitBytes == 0 ? "System commit unavailable." :
        $"System commit {UsedBytes / 1073741824d:0.0}/{LimitBytes / 1073741824d:0.0} GiB." +
        (IsCritical ? " WARNING: commit almost full; further launches can fail even with free RAM. Trimming does not free commit." : "");

    public static SystemCommitStatus Read()
    {
        var data = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        return GetPerformanceInfo(ref data, data.Size)
            ? new(data.CommitTotal.ToUInt64() * data.PageSize.ToUInt64(), data.CommitLimit.ToUInt64() * data.PageSize.ToUInt64())
            : default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable,
            SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation data, uint size);
}
