using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PotatoLauncher;

/// <summary>
/// "Export diagnostics": one zip on the Desktop with a readable report of this PC and its clients, the last days of
/// diagnostics recordings, the optimizer decision log and the optimizer/DLSS settings. Built locally; the user sends
/// it themselves. settings.json is left out on purpose (account details).
/// </summary>
internal static class DiagnosticsExport
{
    /// <param name="system">Counters created at least a second earlier, so the clock reading is a real average.</param>
    public static string Create(IntegratedOptimizerService optimizer, SystemDiagnostics system)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmm");
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"Potato diagnostics {stamp}.zip");
        var root = MainForm.PersistentDataRoot();
        using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
        {
            var report = zip.CreateEntry("report.txt");
            using (var writer = new StreamWriter(report.Open(), new UTF8Encoding(false))) writer.Write(Report(optimizer, system));

            if (Directory.Exists(DiagnosticsRecorder.Folder()))
                foreach (var file in Directory.EnumerateFiles(DiagnosticsRecorder.Folder(), "metrics-*.jsonl"))
                    AddFile(zip, file, "metrics/" + Path.GetFileName(file));
            AddTail(zip, Path.Combine(root, "optimizer-decisions.log"), "optimizer-decisions.log", 5 << 20);
            AddFile(zip, MainForm.OptimizerSettingsPath(), "optimizer.json");
            AddFile(zip, Dlss5Clients.ConfigPath(), "dlss5.json");
        }
        return output;
    }

    internal static string Report(IntegratedOptimizerService optimizer, SystemDiagnostics system)
    {
        var s = optimizer.Settings;
        var text = new StringBuilder();
        void Line(string value = "") => text.AppendLine(value);
        Line($"Potato Launcher diagnostics, {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Line($"Potato: {PotatoVersion()}");
        Line();
        Line("== PC");
        Line($"Windows: {SystemDiagnostics.WindowsVersion()}");
        Line($"CPU: {SystemDiagnostics.CpuName()}, {Environment.ProcessorCount} logical");
        Line($"Layout: {optimizer.Topology.Describe()}");
        var memory = NativeMethods.GetMemoryStatus();
        Line($"RAM: {memory.TotalPhysical / (1024 * 1024)} MB total, {memory.AvailablePhysical / (1024 * 1024)} MB free");
        Line($"Power plan: {SystemDiagnostics.PowerPlan()}");
        Line($"Parked logical processors: {system.ParkedProcessors()?.ToString() ?? "unknown"}");
        Line($"CPU clock: {system.ProcessorPerformance()?.ToString() ?? "unknown"}% of base");
        Line(NvidiaFrameCap.Describe(NvidiaFrameCap.ForGame()));
        var tools = SystemDiagnostics.KnownTools.Where(tool => Process.GetProcessesByName(tool.Process).Length > 0).Select(tool => tool.Label).ToList();
        Line($"Other tools running: {(tools.Count == 0 ? "none of the known ones" : string.Join(", ", tools))}");
        Line();
        Line("== Potato settings");
        Line($"Keep every client at its FPS cap: {s.ClientPolicyEnabled}; target {s.TargetFps}; in-game limit at launch: {s.EnforceInGameFrameLimit}; main on NVIDIA cap: {s.MainUsesNvidiaCap}");
        Line($"CPU placement: setting {s.CpuPlacement}, effective {optimizer.EffectivePlacement}");
        Line(optimizer.PlacementStatus.Replace(Environment.NewLine, Environment.NewLine + "  "));
        Line($"RAM trimming: {s.WorkingSetTrimEnabled} ({s.MemoryTrimMode}); hold runaway/minimized clients: {s.LimitMinimizedClients}");
        var external = optimizer.ExternalChangesLastMinute;
        Line($"Other programs changing clients in the last minute: priority {external.Priority}x, cores {external.Affinity}x");
        Line();
        Line("== Clients (current)");
        var snapshots = optimizer.GetSnapshots();
        Line($"{"Client",-32} {"FPS",5} {"Cap",-15} {"Role",-10} {"CPUs",-8} {"CPU",6} {"GPU",6} {"RAM MB",7}  Injected");
        foreach (var c in snapshots)
        {
            var cap = c.HeldByPotato && c.EngineFrameLimit == 0 ? "held by Potato" : c.EngineFrameLimit switch { null => "?", 0 => "driver", var limit => $"game {limit}" };
            Line($"{Trim(c.ClientName, 32),-32} {(c.Fps is double fps ? fps.ToString("0") : "-"),5} {cap,-15} {c.Role,-10} {ActualCores(c.ProcessId, optimizer.Topology),-8} {c.CpuPercent,5:0.0}% {(c.GpuPercent is double gpu ? gpu.ToString("0.0") + "%" : "-"),6} {c.WorkingSetBytes / (1024 * 1024),7}  {Injected(c.ProcessId)}");
        }
        Line();
        Line("== Findings");
        foreach (var finding in OptimizerDiagnostics.Get(snapshots, s.TargetFps, external)) Line("- " + finding);
        return text.ToString();
    }

    internal static string PotatoVersion() =>
        (Attribute.GetCustomAttribute(typeof(DiagnosticsExport).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
            as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0] ?? "?";

    // What the client really runs on, whoever set it (this Potato, another instance, another tool).
    private static string ActualCores(int pid, CpuTopology topology)
    {
        try { using var process = Process.GetProcessById(pid); return CpuTopology.FormatMask(process.ProcessorAffinity.ToInt64(), topology.AllMask); }
        catch { return "?"; }
    }

    private static string Injected(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var modules = process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToList();
            var found = SystemDiagnostics.KnownInjected
                .Where(known => modules.Any(name => name.Contains(known.Module, StringComparison.OrdinalIgnoreCase)))
                .Select(known => known.Label).ToList();
            return found.Count == 0 ? "-" : string.Join(", ", found);
        }
        catch { return "?"; }
    }

    private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static void AddFile(ZipArchive zip, string path, string name)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }
        catch { }
    }

    private static void AddTail(ZipArchive zip, string path, string name, long maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (source.Length > maxBytes) source.Seek(-maxBytes, SeekOrigin.End);
            using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }
        catch { }
    }
}
