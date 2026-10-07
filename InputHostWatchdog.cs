using System.Diagnostics;

namespace PotatoLauncher;

// Windows' "TextInputHost.exe" (touch keyboard / IME / emoji host) sometimes gets stuck spinning at ~5% of the whole
// CPU, as much as two game clients. Ending it is harmless: Windows recreates it on demand, idle (measured twice:
// 5% -> 0%). The watchdog ends it only after a sustained spin, then waits before acting again.
internal sealed class InputHostWatchdog
{
    internal const string ProcessName = "TextInputHost";
    internal const double SpinThresholdPercent = 2.0;
    internal static readonly TimeSpan SustainedFor = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private DateTime? spinningSince;
    private DateTime lastActionUtc = DateTime.MinValue;
    private (int Pid, DateTime At, TimeSpan Cpu)? previous;

    public int ResetCount { get; private set; }

    // Pure decision: given the CPU share since the last sample, should the host be reset now?
    internal bool ShouldReset(double cpuPercent, DateTime now)
    {
        if (now - lastActionUtc < Cooldown) return false;
        if (cpuPercent < SpinThresholdPercent) { spinningSince = null; return false; }
        spinningSince ??= now;
        return now - spinningSince.Value >= SustainedFor;
    }

    // Returns a status message when it acted, otherwise "".
    public string Tick(DateTime now)
    {
        try
        {
            var host = Process.GetProcessesByName(ProcessName).FirstOrDefault();
            if (host is null) { previous = null; spinningSince = null; return ""; }
            using (host)
            {
                var cpu = host.TotalProcessorTime;
                var sample = (host.Id, now, cpu);
                double percent = 0;
                if (previous is { } last && last.Pid == host.Id && now > last.At)
                    percent = (cpu - last.Cpu).TotalMilliseconds / (now - last.At).TotalMilliseconds / Environment.ProcessorCount * 100;
                previous = sample;
                if (!ShouldReset(percent, now)) return "";
                host.Kill();
                lastActionUtc = now;
                spinningSince = null;
                previous = null;
                ResetCount++;
                return $"Reset Windows' TextInputHost (it was using {percent:0.#}% CPU for {SustainedFor.TotalSeconds:0}s).";
            }
        }
        catch
        {
            return "";
        }
    }
}
