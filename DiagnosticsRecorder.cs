using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PotatoLauncher;

/// <summary>One client in a diagnostics record (values averaged over the record's interval where it applies).</summary>
internal sealed record DiagnosticsClient(
    string Name, int Pid, double? Fps, short? Limit, string Cores, double Cpu, string Priority, bool Minimized, bool Held, string Role);

/// <summary>
/// Writes one JSON line every <see cref="Interval"/> to diagnostics\metrics-yyyyMMdd.jsonl in Potato's data folder,
/// so what happened on a PC (power plan changes, parked cores, CPU per client, FPS) can be read back later from an
/// exported zip. Local only; nothing is sent anywhere. Files older than <see cref="KeepDays"/> are deleted.
/// </summary>
internal sealed class DiagnosticsRecorder : IDisposable
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    internal const int KeepDays = 2;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly SystemDiagnostics system = new();
    private readonly Dictionary<int, (DateTime Start, TimeSpan Cpu)> lastCpu = [];
    private DateTime lastRecordUtc = DateTime.MinValue;
    private (long Idle, long Kernel, long User)? lastTimes;
    private DateTime lastPruneUtc = DateTime.MinValue;
    private string powerPlan = "";
    private DateTime powerPlanReadUtc = DateTime.MinValue;

    private readonly string? folderOverride;

    public DiagnosticsRecorder(string? folder = null) { folderOverride = folder; }

    public static string Folder() => Path.Combine(MainForm.PersistentDataRoot(), "diagnostics");

    private string Target => folderOverride ?? Folder();

    public bool Due(DateTime nowUtc) => nowUtc - lastRecordUtc >= Interval;

    /// <summary>Writes a record if one is due. <paramref name="describe"/> fills everything except CPU, which is measured here.</summary>
    public void Record(DateTime nowUtc, IReadOnlyList<Process> clients, Func<Process, double, DiagnosticsClient?> describe, object extra)
    {
        if (!Due(nowUtc)) return;
        var seconds = lastRecordUtc == DateTime.MinValue ? 0 : (nowUtc - lastRecordUtc).TotalSeconds;
        lastRecordUtc = nowUtc;

        double? systemCpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (lastTimes is { } previous)
            {
                var total = (kernel - previous.Kernel) + (user - previous.User);
                if (total > 0) systemCpu = Math.Round(100d * (total - (idle - previous.Idle)) / total, 1);
            }
            lastTimes = (idle, kernel, user);
        }

        var rows = new List<DiagnosticsClient>();
        foreach (var client in clients)
        {
            double cpu = 0;
            try
            {
                var start = client.StartTime;
                var time = client.TotalProcessorTime;
                if (seconds > 0 && lastCpu.TryGetValue(client.Id, out var before) && before.Start == start)
                    cpu = Math.Round(100d * (time - before.Cpu).TotalSeconds / seconds / Environment.ProcessorCount, 2);
                lastCpu[client.Id] = (start, time);
            }
            catch { }
            if (describe(client, cpu) is { } row) rows.Add(row);
        }
        var alive = clients.Select(client => client.Id).ToHashSet();
        foreach (var gone in lastCpu.Keys.Where(id => !alive.Contains(id)).ToList()) lastCpu.Remove(gone);
        if (seconds == 0) return; // first call only primes the counters

        if (nowUtc - powerPlanReadUtc > TimeSpan.FromMinutes(1)) { powerPlan = SystemDiagnostics.PowerPlan(); powerPlanReadUtc = nowUtc; }
        var line = JsonSerializer.Serialize(new
        {
            t = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            cpu = systemCpu,
            clientCpu = Math.Round(rows.Sum(row => row.Cpu), 2),
            parked = system.ParkedProcessors(),
            clockPercent = system.ProcessorPerformance(),
            powerPlan,
            extra,
            clients = rows
        }, Json);
        try
        {
            Directory.CreateDirectory(Target);
            File.AppendAllText(Path.Combine(Target, $"metrics-{DateTime.Now:yyyyMMdd}.jsonl"), line + Environment.NewLine);
        }
        catch { }
        if (nowUtc - lastPruneUtc > TimeSpan.FromHours(1)) { lastPruneUtc = nowUtc; Prune(); }
    }

    private void Prune()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Target, "metrics-*.jsonl"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays)) File.Delete(file);
        }
        catch { }
    }

    public void Dispose() => system.Dispose();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
