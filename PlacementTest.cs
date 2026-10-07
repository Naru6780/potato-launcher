using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

internal sealed record ClientRef(int ProcessId, DateTime StartUtc);

/// <summary>CPU time and frame counters of every client at one moment; two of them give an exact average.</summary>
internal sealed class LoadSample
{
    public DateTime TakenUtc { get; private init; }
    public long SystemBusy { get; private init; }
    public long SystemTotal { get; private init; }
    public Dictionary<int, (TimeSpan Cpu, uint? Frames)> Clients { get; private init; } = [];

    public static LoadSample Take(IReadOnlyList<ClientRef> clients, ExternalGameState frames)
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        var values = new Dictionary<int, (TimeSpan, uint?)>();
        foreach (var client in clients)
        {
            try
            {
                using var process = Process.GetProcessById(client.ProcessId);
                values[client.ProcessId] = (process.TotalProcessorTime, frames.ReadFrame(client.ProcessId, client.StartUtc)?.FrameCounter);
            }
            catch { }
        }
        // Kernel time includes idle time.
        return new LoadSample { TakenUtc = DateTime.UtcNow, SystemBusy = kernel + user - idle, SystemTotal = kernel + user, Clients = values };
    }

    /// <summary>Percent of the whole CPU (all logical processors) and FPS per client between two samples.</summary>
    public static LoadWindow Between(LoadSample start, LoadSample end)
    {
        var seconds = Math.Max(0.001, (end.TakenUtc - start.TakenUtc).TotalSeconds);
        var total = Math.Max(1, end.SystemTotal - start.SystemTotal);
        var systemCpu = 100d * (end.SystemBusy - start.SystemBusy) / total;
        var clientCpu = 0d;
        var fps = new Dictionary<int, double>();
        var cpu = new Dictionary<int, double>();
        foreach (var (id, after) in end.Clients)
        {
            if (!start.Clients.TryGetValue(id, out var before)) continue;
            var share = 100d * (after.Cpu - before.Cpu).TotalSeconds / seconds / Environment.ProcessorCount;
            cpu[id] = share;
            clientCpu += share;
            if (before.Frames is uint a && after.Frames is uint b) fps[id] = unchecked(b - a) / seconds;
        }
        return new LoadWindow(systemCpu, clientCpu, cpu, fps);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}

internal sealed record LoadWindow(double SystemCpu, double ClientCpu, IReadOnlyDictionary<int, double> CpuByClient, IReadOnlyDictionary<int, double> FpsByClient);

internal sealed record PlacementScore(CpuPlacementMode Mode, double SystemCpu, double ClientCpu, double? MainFps, double? LowestFps, int AtTarget, int Measured);

internal sealed record PlacementTestResult(IReadOnlyList<PlacementScore> Scores, CpuPlacementMode Winner, string Summary);

/// <summary>
/// Tries each placement on the running clients and keeps the one that holds the most clients at the target FPS, then
/// the best main FPS, then the least CPU. Modes are interleaved over several rounds so a scene change in game does
/// not favour one of them. Driven by a 1-second tick; it never blocks.
/// </summary>
internal sealed class PlacementTest
{
    private readonly IReadOnlyList<CpuPlacementMode> order;
    private readonly TimeSpan settle;
    private readonly TimeSpan measure;
    private readonly int targetFps;
    private readonly ExternalGameState frames = new();
    private readonly List<(CpuPlacementMode Mode, LoadWindow Window)> windows = [];
    private int phase;
    private DateTime phaseStartUtc;
    private LoadSample? phaseSample;
    private HashSet<int>? clientIds;

    public PlacementTest(IReadOnlyList<CpuPlacementMode> modes, int rounds, TimeSpan settle, TimeSpan measure, int targetFps, int? mainId)
    {
        order = Enumerable.Range(0, rounds).SelectMany(round => round % 2 == 0 ? modes : modes.Reverse()).ToList();
        this.settle = settle;
        this.measure = measure;
        this.targetFps = targetFps;
        MainId = mainId;
    }

    public int? MainId { get; }
    public bool Done { get; private set; }
    public string? Error { get; private set; }
    public PlacementTestResult? Result { get; private set; }
    public CpuPlacementMode? CurrentMode => Done ? null : order[phase];
    public TimeSpan Remaining => TimeSpan.FromSeconds((order.Count - phase) * (settle + measure).TotalSeconds - (phaseStartUtc == default ? 0 : (DateTime.UtcNow - phaseStartUtc).TotalSeconds));

    public string Status => Done
        ? Error ?? Result?.Summary ?? ""
        : $"Testing placements: {Label(order[phase])} ({phase + 1}/{order.Count}), about {Math.Max(0, Remaining.TotalSeconds):0} s left. Leave the clients where they are.";

    public void Cancel(string reason)
    {
        if (Done) return;
        Done = true;
        Error = reason;
    }

    /// <summary>Call about once a second while CurrentMode is applied to the clients.</summary>
    public void Tick(DateTime nowUtc, IReadOnlyList<ClientRef> clients)
    {
        if (Done) return;
        var ids = clients.Select(client => client.ProcessId).ToHashSet();
        clientIds ??= ids;
        if (!clientIds.SetEquals(ids)) { Cancel("Placement test stopped: a client started or closed. Run it again with every client in game."); return; }
        if (phaseStartUtc == default) phaseStartUtc = nowUtc;

        var elapsed = nowUtc - phaseStartUtc;
        if (phaseSample is null && elapsed >= settle) phaseSample = LoadSample.Take(clients, frames);
        if (phaseSample is null || elapsed < settle + measure) return;

        windows.Add((order[phase], LoadSample.Between(phaseSample, LoadSample.Take(clients, frames))));
        phaseSample = null;
        phaseStartUtc = nowUtc;
        if (++phase < order.Count) return;
        Done = true;
        Result = Score(windows, targetFps, MainId);
    }

    internal static PlacementTestResult Score(IReadOnlyList<(CpuPlacementMode Mode, LoadWindow Window)> windows, int targetFps, int? mainId)
    {
        var scores = windows.GroupBy(entry => entry.Mode).Select(group =>
        {
            var fpsByClient = group.SelectMany(entry => entry.Window.FpsByClient)
                .GroupBy(entry => entry.Key)
                .ToDictionary(entries => entries.Key, entries => entries.Average(entry => entry.Value));
            return new PlacementScore(
                group.Key,
                group.Average(entry => entry.Window.SystemCpu),
                group.Average(entry => entry.Window.ClientCpu),
                mainId is int main && fpsByClient.TryGetValue(main, out var mainFps) ? mainFps : null,
                fpsByClient.Count == 0 ? null : fpsByClient.Values.Min(),
                fpsByClient.Values.Count(fps => fps >= targetFps - AtTargetSlack),
                fpsByClient.Count);
        }).OrderBy(score => score.Mode).ToList();

        var winner = scores[0];
        foreach (var candidate in scores.Skip(1)) if (Better(candidate, winner)) winner = candidate;
        var lines = scores.Select(score =>
            $"{Label(score.Mode)}: CPU {score.SystemCpu:0.0}% (clients {score.ClientCpu:0.0}%), {score.AtTarget}/{score.Measured} at {targetFps}" +
            (score.MainFps is double main ? $", main {main:0}" : "") +
            (score.LowestFps is double low ? $", lowest {low:0}" : ""));
        var summary = $"Best: {Label(winner.Mode)}.  " + string.Join("  ·  ", lines);
        return new PlacementTestResult(scores, winner.Mode, summary);
    }

    /// <summary>Bumped when the scoring changes, so results stored by an older version are measured again.</summary>
    // 3: the test now uses the configured main (1.0.120/121 measured without it when no window had been clicked).
    public const int ScoringVersion = 3;

    // The game's own 60 fps limiter delivers ~58.0-58.2, so a 2 FPS slack put clients on the threshold and 0.1 FPS of
    // noise decided the winner (seen on a 9800X3D). 3 FPS matches "at cap" everywhere else in the Optimizer.
    internal const int AtTargetSlack = 3;

    // Between runs on the same PC the CPU of one placement varied by up to ~1.7 points.
    internal const double CpuTolerance = 2.0;

    // More clients at target wins; then a main FPS at least 2 higher; then less total CPU by more than the run-to-run
    // noise. Total (system) CPU is what Task Manager shows and includes the scheduling cost of spreading clients over
    // every core, which per-process CPU time does not. Otherwise the simpler placement (lower enum value) stays.
    private static bool Better(PlacementScore candidate, PlacementScore current)
    {
        if (candidate.AtTarget != current.AtTarget) return candidate.AtTarget > current.AtTarget;
        if (candidate.MainFps is double a && current.MainFps is double b && Math.Abs(a - b) >= 2) return a > b;
        return candidate.SystemCpu <= current.SystemCpu - CpuTolerance;
    }

    public static string Label(CpuPlacementMode mode) => mode switch
    {
        CpuPlacementMode.Off => "No pinning",
        CpuPlacementMode.ReserveMain => "Main gets its own cores",
        CpuPlacementMode.Lanes => "Two-core lanes",
        _ => "Main gets the cache CCD"
    };
}
