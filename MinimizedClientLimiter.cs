using System.Runtime.InteropServices;

namespace PotatoLauncher;

// A minimized FFXIV stops presenting frames, so nothing paces it any more (not the display, not the GPU driver's frame
// cap, and Windows' timer/EcoQoS throttling has no effect on it): its loop spins at 250-400 iterations per second and
// costs 2-3x the CPU of a visible client. While a client is minimized, Potato puts a hard CPU-time cap on it (a Windows
// job object) and steers that cap from the measured loop rate so the client keeps running at the target (60).
// Restoring the window removes the cap immediately. Measured on a 9800X3D: 271 loops/s at 7.5% CPU -> 87 at 3.5% with
// a fixed 3% cap; the feedback loop below lands it on the target instead.
internal sealed class MinimizedClientLimiter : IDisposable
{
    // Job CPU rate is in 1/100 of a percent of the whole machine.
    internal const uint InitialCap = 210; // ~60 loops/s on a 9800X3D (measured: 3% -> 87, 4% -> 125)
    internal const uint MinimumCap = 50;
    internal const uint MaximumCap = 2500;
    internal const double Tolerance = 4;

    private readonly Dictionary<int, Entry> entries = [];

    private sealed class Entry
    {
        public IntPtr Job;
        public DateTime StartUtc;
        public uint Cap; // 0 = not capped
        public DateTime LastAdjustUtc;
        public double? SmoothedFps;
    }

    // Next cap from the measured loop rate: proportional step toward the target, bounded so one noisy sample
    // cannot swing it far.
    // Windows enforces the cap over its own accounting interval, so the loop rate reacts with a delay: steps are damped
    // (square root of the error ratio, at most -15%/+20%) and taken at most every AdjustInterval to avoid oscillation.
    internal static readonly TimeSpan AdjustInterval = TimeSpan.FromSeconds(3);

    internal static uint NextCap(uint current, double fps, double targetFps)
    {
        if (current == 0) return InitialCap;
        if (Math.Abs(fps - targetFps) <= Tolerance || fps <= 0) return current;
        var ratio = Math.Clamp(Math.Sqrt(targetFps / fps), 0.85, 1.2);
        var next = (uint)Math.Clamp(Math.Round(current * ratio), MinimumCap, MaximumCap);
        return next == current ? (uint)Math.Clamp((long)current + (fps > targetFps ? -5 : 5), MinimumCap, MaximumCap) : next;
    }

    public void Update(int processId, DateTime startUtc, bool minimized, double? fps, double targetFps)
    {
        entries.TryGetValue(processId, out var entry);
        if (entry is not null && entry.StartUtc != startUtc)
        {
            Release(processId);
            entry = null;
        }

        if (!minimized)
        {
            if (entry is { Cap: > 0 }) SetCap(entry, 0);
            if (entry is not null) entry.SmoothedFps = null;
            return;
        }

        if (entry is null)
        {
            entry = new Entry { StartUtc = startUtc };
            if (!TryAttach(processId, entry)) return;
            entries[processId] = entry;
        }

        var now = DateTime.UtcNow;
        if (entry.Cap == 0)
        {
            SetCap(entry, InitialCap);
            entry.LastAdjustUtc = now;
            entry.SmoothedFps = null;
            return;
        }
        // Capped loops run in bursts, so per-second readings are noisy: smooth them before steering.
        if (fps is double measured) entry.SmoothedFps = entry.SmoothedFps is double previous ? previous * 0.6 + measured * 0.4 : measured;
        if (entry.SmoothedFps is not double smoothed || now - entry.LastAdjustUtc < AdjustInterval) return;
        var next = NextCap(entry.Cap, smoothed, targetFps);
        entry.LastAdjustUtc = now;
        if (next != entry.Cap) SetCap(entry, next);
    }

    public bool IsCapped(int processId) => entries.TryGetValue(processId, out var entry) && entry.Cap > 0;

    public void Forget(IEnumerable<int> aliveProcessIds)
    {
        var alive = aliveProcessIds.ToHashSet();
        foreach (var gone in entries.Keys.Where(id => !alive.Contains(id)).ToList()) Release(gone);
    }

    public void Dispose()
    {
        foreach (var id in entries.Keys.ToList()) Release(id);
    }

    private void Release(int processId)
    {
        if (!entries.Remove(processId, out var entry)) return;
        if (entry.Cap > 0) SetCap(entry, 0);
        if (entry.Job != IntPtr.Zero) CloseHandle(entry.Job); // the process simply stays in an uncapped job
    }

    private static bool TryAttach(int processId, Entry entry)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return false;
        var process = OpenProcess(ProcessSetQuota | ProcessTerminate | ProcessQueryLimitedInformation, false, processId);
        try
        {
            if (process == IntPtr.Zero || !AssignProcessToJobObject(job, process))
            {
                CloseHandle(job);
                return false;
            }
            entry.Job = job;
            return true;
        }
        finally
        {
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    private static void SetCap(Entry entry, uint cap)
    {
        var info = new CpuRateControl { ControlFlags = cap == 0 ? 0 : RateEnable | RateHardCap, CpuRate = cap };
        if (SetInformationJobObject(entry.Job, JobObjectCpuRateControlInformation, ref info, Marshal.SizeOf<CpuRateControl>()))
            entry.Cap = cap;
    }

    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int JobObjectCpuRateControlInformation = 15;
    private const uint RateEnable = 0x1;
    private const uint RateHardCap = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuRateControl
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref CpuRateControl info, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
