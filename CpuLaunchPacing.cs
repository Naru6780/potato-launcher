using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

internal sealed class CpuCalmWindow(int thresholdPercent)
{
    public int CalmReadings { get; private set; }
    public bool Observe(double? usage)
    {
        CalmReadings = usage is double value && double.IsFinite(value) && value >= 0 && value < thresholdPercent
            ? CalmReadings + 1 : 0;
        return CalmReadings >= 3;
    }
}

internal static class CpuLaunchPacing
{
    public static async Task WaitAsync(int thresholdPercent, Action<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Independent counter: the monitor must not consume this sampler's baseline.
        // Processor Information _Total covers all processor groups.
        using var counter = await Task.Run(() =>
        {
            return new WindowsCpuCounter(); // Constructor warms up; never a calm reading.
        });
        var calm = new CpuCalmWindow(Math.Clamp(thresholdPercent, 40, 95));
        var elapsed = Stopwatch.StartNew();
        progress($"Waiting for CPU below {thresholdPercent}% for three consecutive seconds. Cancel stops the queue.");
        while (true)
        {
            await Task.Delay(1000, token);
            // A counter failure aborts the queue; it must never masquerade as 0% usage.
            var usage = await Task.Run(() => (double)counter.NextValue(), token);
            if (!double.IsFinite(usage) || usage < 0)
                throw new InvalidOperationException("CPU usage could not be measured. No next client was started.");
            var ready = calm.Observe(usage);
            progress($"CPU {usage:0}% — waiting below {thresholdPercent}%: {calm.CalmReadings}/3 calm readings. Cancel stops the queue.");
            token.ThrowIfCancellationRequested();
            if (ready) return;
            if (elapsed.Elapsed > TimeSpan.FromMinutes(5))
                throw new TimeoutException($"CPU did not settle below {thresholdPercent}% for five minutes. Queue stopped; existing game clients are unchanged.");
        }
    }
}

// English PDH paths are translated by Windows, including on non-English installs.
internal sealed class WindowsCpuCounter : IDisposable
{
    private IntPtr query;
    private IntPtr counter;
    public WindowsCpuCounter()
    {
        try
        {
            Check(PdhOpenQueryW(null, UIntPtr.Zero, out query));
            Check(PdhAddEnglishCounterW(query, @"\Processor Information(_Total)\% Processor Time", UIntPtr.Zero, out counter));
            Check(PdhCollectQueryData(query));
        }
        catch { Dispose(); throw; }
    }

    public double NextValue()
    {
        ObjectDisposedException.ThrowIf(query == IntPtr.Zero, this);
        Check(PdhCollectQueryData(query));
        Check(PdhGetFormattedCounterValue(counter, 0x200, out _, out var value));
        if (value.Status > 1 || !double.IsFinite(value.Value) || value.Value < 0)
            throw new InvalidOperationException("CPU measurement is unavailable. Launch queue stopped.");
        return value.Value;
    }

    private static void Check(uint status)
    {
        if (status != 0) throw new InvalidOperationException($"Windows CPU counter failed (0x{status:X8}). No next client was started.");
    }

    public void Dispose()
    {
        if (query == IntPtr.Zero) return;
        PdhCloseQuery(query);
        query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? source, UIntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
