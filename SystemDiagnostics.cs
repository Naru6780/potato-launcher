using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PotatoLauncher;

/// <summary>
/// Read-only facts about the PC that decide how much CPU the clients cost and that screenshots do not show: the
/// active power plan, how many logical processors Windows has parked, and the CPU's effective clock.
/// </summary>
internal sealed class SystemDiagnostics : IDisposable
{
    private PerformanceCounter? performance;
    private PerformanceCounter[] parking = [];
    private bool initialized;

    /// <summary>"% Processor Performance": 100 = base clock, above 100 = boosting, well below = slowed down.</summary>
    public double? ProcessorPerformance()
    {
        EnsureCounters();
        try { return performance is null ? null : Math.Round(performance.NextValue(), 0); } catch { return null; }
    }

    /// <summary>Logical processors currently parked by Windows (Balanced plans park idle cores; High performance does not).</summary>
    public int? ParkedProcessors()
    {
        EnsureCounters();
        if (parking.Length == 0) return null;
        var parked = 0;
        foreach (var counter in parking)
        {
            try { if (counter.NextValue() != 0) parked++; } catch { return null; }
        }
        return parked;
    }

    private void EnsureCounters()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            performance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true);
            _ = performance.NextValue();
            var instances = new PerformanceCounterCategory("Processor Information").GetInstanceNames()
                .Where(name => !name.Contains("_Total", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name)
                .ToArray();
            parking = instances.Select(name => new PerformanceCounter("Processor Information", "Parking Status", name, readOnly: true)).ToArray();
        }
        catch
        {
            performance?.Dispose();
            performance = null;
            foreach (var counter in parking) counter.Dispose();
            parking = [];
        }
    }

    public void Dispose()
    {
        performance?.Dispose();
        foreach (var counter in parking) counter.Dispose();
    }

    /// <summary>Name of the active Windows power plan, e.g. "Balanced" or "High performance".</summary>
    public static string PowerPlan()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var schemePointer) != 0) return "unknown";
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(schemePointer);
                uint size = 0;
                PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
                if (size == 0) return scheme.ToString();
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return scheme.ToString();
                    return $"{Marshal.PtrToStringUni(buffer)} ({scheme})";
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { LocalFree(schemePointer); }
        }
        catch { return "unknown"; }
    }

    public static string CpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "unknown";
        }
        catch { return "unknown"; }
    }

    public static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var build = key?.GetValue("CurrentBuild") as string ?? "";
            // Windows 11 still reports "Windows 10" in ProductName; the build number tells them apart.
            if (int.TryParse(build, out var number) && number >= 22000) product = product.Replace("Windows 10", "Windows 11");
            return $"{product} {key?.GetValue("DisplayVersion")} build {build}.{key?.GetValue("UBR")}";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    /// <summary>Programs known to change game priorities, cores or frame pacing, by process name.</summary>
    internal static readonly (string Process, string Label)[] KnownTools =
    [
        ("ProcessGovernor", "Process Lasso (core engine)"), ("ProcessLasso", "Process Lasso"),
        ("RTSS", "RivaTuner Statistics Server"), ("MSIAfterburner", "MSI Afterburner"),
        ("Medal", "Medal"), ("Overwolf", "Overwolf"), ("NVIDIA Overlay", "NVIDIA overlay"),
        ("Parsec", "Parsec"), ("obs64", "OBS Studio"), ("XboxGameBar", "Xbox Game Bar")
    ];

    /// <summary>DLLs that third-party tools inject into every game client.</summary>
    internal static readonly (string Module, string Label)[] KnownInjected =
    [
        ("DiscordHook64", "Discord overlay"), ("medal-hook64", "Medal"), ("RTSSHooks64", "RivaTuner"),
        ("GameOverlayRenderer64", "Steam overlay"), ("nvspcap64", "NVIDIA ShadowPlay"), ("ow-graphics", "Overwolf"),
        ("renodx", "RenoDX (DLSS 5 add-on)"), ("ReShade", "ReShade")
    ];

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroup, IntPtr setting, IntPtr buffer, ref uint bufferSize);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
