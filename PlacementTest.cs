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
    // CPU time of every other program (not the clients, not Idle/System): detects outside load changing mid-test.
    public TimeSpan OtherCpu { get; private init; }

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
        var other = TimeSpan.Zero;
        var clientIds = clients.Select(client => client.ProcessId).ToHashSet();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id <= 4 || clientIds.Contains(process.Id)) continue;
                try { other += process.TotalProcessorTime; } catch { }
            }
        }
        // Kernel time includes idle time.
        return new LoadSample { TakenUtc = DateTime.UtcNow, SystemBusy = kernel + user - idle, SystemTotal = kernel + user, Clients = values, OtherCpu = other };
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
        // Processes that start or exit between the samples make this approximate; it only needs to catch big swings.
        var otherCpu = Math.Max(0, 100d * (end.OtherCpu - start.OtherCpu).TotalSeconds / seconds / Environment.ProcessorCount);
        return new LoadWindow(systemCpu, clientCpu, cpu, fps, otherCpu);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}

internal sealed record LoadWindow(double SystemCpu, double ClientCpu, IReadOnlyDictionary<int, double> CpuByClient, IReadOnlyDictionary<int, double> FpsByClient, double OtherCpu = 0,
    IReadOnlyDictionary<int, IReadOnlyList<double>>? FpsSamples = null);

/// <summary>MainFps and LowestFps are "steady" values: the 5th percentile of per-second FPS when sampled (a client that
/// dips is judged by its dips, not its average), else the window average.</summary>
internal sealed record PlacementScore(CpuPlacementMode Mode, double SystemCpu, double ClientCpu, double? MainFps, double? LowestFps, int AtTarget, int Measured,
    double? MainAverage = null);

internal sealed record PlacementTestResult(IReadOnlyList<PlacementScore> Scores, CpuPlacementMode Winner, string Summary, bool Inconclusive = false);

/// <summary>
/// Tries each placement on the running clients and keeps the one that holds the main steady at the target FPS (or
/// closest to it), then the most clients at target, then the least CPU. Modes are interleaved over several rounds so
/// a scene change in game does not favour one of them. Driven by a 1-second tick; it never blocks.
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
    private DateTime lastTickUtc;
    private readonly Dictionary<int, List<double>> phaseFps = [];
    internal static readonly TimeSpan MaxTickGap = TimeSpan.FromSeconds(5);

    public PlacementTest(IReadOnlyList<CpuPlacementMode> modes, int rounds, TimeSpan settle, TimeSpan measure, int targetFps, int? mainId,
        CpuPlacementMode? preferred = null)
    {
        order = Enumerable.Range(0, rounds).SelectMany(round => round % 2 == 0 ? modes : modes.Reverse()).ToList();
        this.settle = settle;
        this.measure = measure;
        this.targetFps = targetFps;
        MainId = mainId;
        Preferred = preferred;
    }

    public int? MainId { get; }
    public CpuPlacementMode? Preferred { get; }
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
    /// <param name="liveFps">Each client's current FPS (about one value per second), recorded while measuring.</param>
    public void Tick(DateTime nowUtc, IReadOnlyList<ClientRef> clients, IReadOnlyDictionary<int, double>? liveFps = null)
    {
        if (Done) return;
        var ids = clients.Select(client => client.ProcessId).ToHashSet();
        clientIds ??= ids;
        if (!clientIds.SetEquals(ids)) { Cancel("Placement test stopped: a client started or closed. Run it again with every client in game."); return; }
        // A CPU starved by runaway clients delays Potato's own timer; a phase measured across such a gap is meaningless
        // (seen: one tick 20+ s late gave a near-zero window, "clients 649%" and "main 1155 FPS").
        if (lastTickUtc != default && nowUtc - lastTickUtc > MaxTickGap)
        {
            Cancel($"Placement test stopped: Potato itself got no CPU time for {(nowUtc - lastTickUtc).TotalSeconds:0} s, so the CPU is saturated. Make sure every client has its in-game frame limit (Cap column: game 60), then run it again.");
            return;
        }
        lastTickUtc = nowUtc;
        if (phaseStartUtc == default) phaseStartUtc = nowUtc;

        var elapsed = nowUtc - phaseStartUtc;
        if (phaseSample is null && elapsed >= settle) phaseSample = LoadSample.Take(clients, frames);
        if (phaseSample is not null && liveFps is not null)
            foreach (var id in ids)
                if (liveFps.TryGetValue(id, out var fps)) (phaseFps.TryGetValue(id, out var list) ? list : phaseFps[id] = []).Add(fps);
        // The window is timed from its own first sample, so it always spans the full measuring time.
        if (phaseSample is null || nowUtc - phaseSample.TakenUtc < measure) return;

        var window = LoadSample.Between(phaseSample, LoadSample.Take(clients, frames)) with
        {
            FpsSamples = phaseFps.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<double>)entry.Value.ToArray())
        };
        phaseFps.Clear();
        if (window.SystemCpu > 100.5 || window.ClientCpu > 100.5)
        {
            Cancel("Placement test stopped: a measurement came out impossible (over 100% CPU). Run it again.");
            return;
        }
        windows.Add((order[phase], window));
        phaseSample = null;
        phaseStartUtc = nowUtc;
        if (++phase < order.Count) return;
        Done = true;
        Result = Score(windows, targetFps, MainId, Preferred);
    }

    // Outside load (other programs) moving more than this during the test swamps the ~2-4 point differences measured.
    internal const double OtherCpuMaxSpread = 3.0;
    // Same placement, different rounds: more than this apart means the scene changed (client CPU, % of the whole CPU).
    internal const double SceneDriftMax = 5.0;
    // Per-second FPS readings needed per client before the 5th percentile is used instead of the average.
    internal const int MinimumSamples = 10;

    /// <param name="preferred">The placement kept when results are within the noise: the topology default, which two
    /// clean runs measured as cheaper than no pinning (a tie used to fall back to no pinning).</param>
    internal static PlacementTestResult Score(IReadOnlyList<(CpuPlacementMode Mode, LoadWindow Window)> windows, int targetFps, int? mainId,
        CpuPlacementMode? preferred = null)
    {
        var scores = windows.GroupBy(entry => entry.Mode).Select(group =>
        {
            var fpsByClient = group.SelectMany(entry => entry.Window.FpsByClient)
                .GroupBy(entry => entry.Key)
                .ToDictionary(entries => entries.Key, entries => entries.Average(entry => entry.Value));
            // Steady FPS per client: 5th percentile of its per-second readings across this mode's windows.
            var steady = fpsByClient.ToDictionary(entry => entry.Key, entry =>
            {
                var samples = group.SelectMany(window => window.Window.FpsSamples is { } all && all.TryGetValue(entry.Key, out var list) ? list : [])
                    .OrderBy(value => value).ToArray();
                return samples.Length >= MinimumSamples ? samples[(int)Math.Floor((samples.Length - 1) * 0.05)] : entry.Value;
            });
            return new PlacementScore(
                group.Key,
                group.Average(entry => entry.Window.SystemCpu),
                group.Average(entry => entry.Window.ClientCpu),
                mainId is int main && steady.TryGetValue(main, out var mainSteady) ? mainSteady : null,
                steady.Count == 0 ? null : steady.Values.Min(),
                steady.Values.Count(fps => fps >= targetFps - AtTargetSlack),
                steady.Count,
                mainId is int m && fpsByClient.TryGetValue(m, out var mainAverage) ? mainAverage : null);
        }).OrderBy(score => score.Mode).ToList();

        var winner = scores.FirstOrDefault(score => score.Mode == preferred) ?? scores[0];
        foreach (var candidate in scores.Where(score => score != winner)) if (Better(candidate, winner, targetFps)) winner = candidate;
        var lines = scores.Select(score =>
            $"{Label(score.Mode)}: CPU {score.SystemCpu:0.0}% (clients {score.ClientCpu:0.0}%), {score.AtTarget}/{score.Measured} at {targetFps}" +
            (score.MainAverage is double average ? $", main {average:0}" : "") +
            (score.MainFps is double main ? $" (worst {main:0})" : "") +
            (score.LowestFps is double low ? $", lowest {low:0}" : ""));
        // The game itself getting busier or quieter mid-test shows as the SAME placement measuring differently between
        // rounds (seen: client CPU 43% -> 64% within two minutes on a 9950X3D). Then the comparison is not fair.
        var drift = windows.GroupBy(entry => entry.Mode).Where(group => group.Count() > 1)
            .Select(group => group.Max(entry => entry.Window.ClientCpu) - group.Min(entry => entry.Window.ClientCpu))
            .DefaultIfEmpty(0).Max();
        if (drift > SceneDriftMax)
        {
            var busy = $"Inconclusive: the game itself got busier or quieter during the test (the same placement measured {drift:0.0} points apart). " +
                       "Your current placement was kept. Run it again somewhere calm, with every client standing still.  " + string.Join("  Â·  ", lines);
            return new PlacementTestResult(scores, winner.Mode, busy, Inconclusive: true);
        }
        var others = windows.Select(entry => entry.Window.OtherCpu).ToList();
        var spread = others.Count == 0 ? 0 : others.Max() - others.Min();
        if (spread > OtherCpuMaxSpread)
        {
            var noisy = $"Inconclusive: other programs' CPU use changed by {spread:0.0} points during the test, more than the differences it measures. " +
                        "Your current placement was kept. Close heavy programs (compiles, video exports, analysis tools) and run it again.  " + string.Join("  ·  ", lines);
            return new PlacementTestResult(scores, winner.Mode, noisy, Inconclusive: true);
        }
        var summary = $"Best: {Label(winner.Mode)}.  " + string.Join("  ·  ", lines);
        return new PlacementTestResult(scores, winner.Mode, summary);
    }

    /// <summary>Bumped when the scoring changes, so results stored by an older version are measured again.</summary>
    // 3: the test now uses the configured main (1.0.120/121 measured without it when no window had been clicked).
    // 4: windows timed from their own sample, starved/impossible runs rejected (a 1.0.122 run stored garbage).
    // 5: outside-load check and ties keep the default (a 1.0.125 run during a background analysis stored "No pinning").
    // 6: main-core reservation rules changed in 1.0.128 (floor of 2 cores, regrow after a minute).
    // 7: steadiness (5th percentile of per-second FPS) decides before CPU; scene drift makes a run inconclusive.
    // 8: the main decides first (1.0.130); main-core sizing changed, so every placement measures differently.
    public const int ScoringVersion = 8;

    // The game's own 60 fps limiter delivers ~58.0-58.2, so a 2 FPS slack put clients on the threshold and 0.1 FPS of
    // noise decided the winner (seen on a 9800X3D). 3 FPS matches "at cap" everywhere else in the Optimizer.
    internal const int AtTargetSlack = 3;

    // Between runs on the same PC the CPU of one placement varied by up to ~1.7 points.
    internal const double CpuTolerance = 2.0;

    // The main decides first: steady at target beats not, and while neither is, 2+ FPS more on the main wins (a 9950X3D
    // run with no placement at target picked "4/16 at 60, main 43" over "main 45"). Then more clients at target; then a
    // main at least 2 higher; then less total CPU by more than the run-to-run noise. Total (system) CPU is what Task
    // Manager shows and includes the scheduling cost of spreading clients over every core, which per-process CPU time
    // does not. Otherwise the simpler placement (lower enum value) stays.
    private static bool Better(PlacementScore candidate, PlacementScore current, int targetFps)
    {
        if (candidate.MainFps is double a && current.MainFps is double b)
        {
            var (candidateAtTarget, currentAtTarget) = (a >= targetFps - AtTargetSlack, b >= targetFps - AtTargetSlack);
            if (candidateAtTarget != currentAtTarget) return candidateAtTarget;
            if (!candidateAtTarget && Math.Abs(a - b) >= 2) return a > b;
        }
        if (candidate.AtTarget != current.AtTarget) return candidate.AtTarget > current.AtTarget;
        if (candidate.MainFps is double x && current.MainFps is double y && Math.Abs(x - y) >= 2) return x > y;
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
