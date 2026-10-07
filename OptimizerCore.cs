using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotatoLauncher;

internal enum MemoryTrimMode
{
    PressureAware,
    Threshold
}

internal sealed class MainClientRule
{
    public string ClientName { get; set; } = "";
    public int Priority { get; set; }
}

internal sealed record MainClientIdentity(int ProcessId, string ClientName, DateTime StartTime);
internal sealed record MainClientSelection(HashSet<int> ActiveMainClientIds, HashSet<int> CandidateClientIds);

internal static class MainClientSelector
{
    public static MainClientSelection Select(IReadOnlyList<MainClientIdentity> clients, OptimizerSettings settings)
    {
        var candidates = clients
            .Where(client => settings.IsMainCandidate(client.ClientName))
            .OrderBy(client => settings.GetMainPriority(client.ClientName))
            .ThenBy(client => client.StartTime)
            .ThenBy(client => client.ProcessId)
            .ToList();
        var candidateIds = candidates.Select(client => client.ProcessId).ToHashSet();
        var activeId = candidates.FirstOrDefault()?.ProcessId;
        if (!activeId.HasValue && clients.Count > 0)
        {
            activeId = clients.OrderBy(client => client.StartTime).ThenBy(client => client.ProcessId).First().ProcessId;
        }
        return new MainClientSelection(activeId.HasValue ? [activeId.Value] : [], candidateIds);
    }
}

internal static class MemoryPressurePolicy
{
    public static bool Evaluate(bool currentlyActive, double usedPercent, double availableMemoryMb, OptimizerSettings settings)
    {
        return currentlyActive
            ? usedPercent > settings.MemoryPressureStopPercent || availableMemoryMb < settings.CriticalAvailableMemoryMB * 1.5
            : usedPercent >= settings.MemoryPressureStartPercent || availableMemoryMb <= settings.CriticalAvailableMemoryMB;
    }
}

internal sealed class OptimizerSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // CPU placement (CpuPlacement.cs). Auto = the measured winner of "Test placements" on this CPU, else pinning only
    // where cores are unequal (two CCDs, P/E cores). The 1.0.118 lane keys are ignored on load.
    public CpuPlacementSetting CpuPlacement { get; set; } = CpuPlacementSetting.Auto;
    public CpuPlacementMode? TestedPlacement { get; set; }
    public string TestedPlacementTopology { get; set; } = "";
    public string TestedPlacementSummary { get; set; } = "";
    public bool WorkingSetTrimEnabled { get; set; } = true;
    public MemoryTrimMode MemoryTrimMode { get; set; } = MemoryTrimMode.PressureAware;
    public int TrimTriggerMBPerClient { get; set; } = 1024;
    public int TrimIntervalSeconds { get; set; } = 10;
    public int TrimCooldownSeconds { get; set; } = 30;
    public List<int> ManualMainClientIds { get; set; } = [];
    public List<MainClientRule> MainClientRules { get; set; } = [];
    // Client policy (see ClientPolicy.cs): keeps every client at its frame cap.
    public bool ClientPolicyEnabled { get; set; } = true;
    public bool FollowForegroundClient { get; set; } = true;
    public ProcessPriorityClass ActiveClientPriority { get; set; } = ProcessPriorityClass.AboveNormal;
    public ProcessPriorityClass BackgroundClientPriority { get; set; } = ProcessPriorityClass.Normal;
    public bool PreventWindowsThrottling { get; set; } = true;
    public bool LowMemoryPriorityForBackground { get; set; } = true;
    // Minimized clients stop presenting and spin far above their cap; hold them at TargetFps with a hard CPU cap.
    public bool LimitMinimizedClients { get; set; } = true;
    // The frame cap every client should hold; used for "at cap" status and the capacity estimate.
    public int TargetFps { get; set; } = 60;
    // Set the game's own frame limiter (FFXIV.cfg "Fps") to the option matching TargetFps before every launch.
    public bool EnforceInGameFrameLimit { get; set; } = true;
    // Opt-in (off by default since 1.0.125): the configured main launches with Frame Rate None so the NVIDIA cap holds
    // it at an exact target, only when Potato reads that cap at the target. The game's own limit is cheaper (measured
    // 6.26% vs 7.07% CPU on the main), so it stays the default for every client. 1.0.124's "mainUsesDriverCap" key
    // (default on) is deliberately not read.
    public bool MainUsesNvidiaCap { get; set; }
    // End Windows' TextInputHost when it spins (~5% CPU, harmless to restart; Windows recreates it idle).
    public bool ResetSpinningInputHost { get; set; } = true;
    public int MemoryPressureStartPercent { get; set; } = 85;
    public int MemoryPressureStopPercent { get; set; } = 75;
    public int CriticalAvailableMemoryMB { get; set; } = 4096;

    public static OptimizerSettings Load()
    {
        try
        {
            var path = MainForm.OptimizerSettingsPath();
            if (!File.Exists(path))
            {
                var defaults = new OptimizerSettings();
                defaults.Save();
                return defaults;
            }

            var json = File.ReadAllText(path);
            var settings = DeserializeCompatible(json);
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new OptimizerSettings();
        }
    }

    internal static OptimizerSettings DeserializeCompatible(string json)
    {
        // Preserve the rest of a profile created by the superseded v107 rather
        // than discarding all settings when its new enum names are encountered.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject
            ?? throw new JsonException("Optimizer settings must be an object.");
        if (node["memoryTrimMode"]?.ToString() is "BandBudget" or "2")
            node["memoryTrimMode"] = "Threshold";
        return node.Deserialize<OptimizerSettings>(JsonOptions) ?? new OptimizerSettings();
    }

    public void Save()
    {
        Normalize();
        var path = MainForm.OptimizerSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicTextFile.Write(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public bool IsMainCandidate(string clientName)
    {
        var key = NormalizeClientName(clientName);
        return MainClientRules.Any(rule => string.Equals(NormalizeClientName(rule.ClientName), key, StringComparison.OrdinalIgnoreCase));
    }

    public int GetMainPriority(string clientName)
    {
        var key = NormalizeClientName(clientName);
        return MainClientRules.FirstOrDefault(rule => string.Equals(NormalizeClientName(rule.ClientName), key, StringComparison.OrdinalIgnoreCase))?.Priority ?? 0;
    }

    public void SetMainCandidate(string clientName, bool isMain)
    {
        var key = NormalizeClientName(clientName);
        if (string.IsNullOrWhiteSpace(key)) return;
        MainClientRules.RemoveAll(rule => string.Equals(NormalizeClientName(rule.ClientName), key, StringComparison.OrdinalIgnoreCase));
        if (isMain)
        {
            MainClientRules.Add(new MainClientRule
            {
                ClientName = key,
                Priority = MainClientRules.Count == 0 ? 1 : MainClientRules.Max(rule => rule.Priority) + 1
            });
        }
        Normalize();
    }

    public void SetMainPriority(string clientName, int priority)
    {
        var key = NormalizeClientName(clientName);
        var rule = MainClientRules.FirstOrDefault(candidate => string.Equals(NormalizeClientName(candidate.ClientName), key, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return;
        var ordered = MainClientRules.OrderBy(candidate => candidate.Priority).ToList();
        ordered.Remove(rule);
        ordered.Insert(Math.Clamp(priority - 1, 0, ordered.Count), rule);
        MainClientRules = ordered.Select((candidate, index) => new MainClientRule
        {
            ClientName = NormalizeClientName(candidate.ClientName),
            Priority = index + 1
        }).ToList();
        Normalize();
    }

    internal static ProcessPriorityClass ClampClientPriority(ProcessPriorityClass value, ProcessPriorityClass fallback) =>
        value is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal ? value : fallback;

    public void Normalize()
    {
        if (!Enum.IsDefined(MemoryTrimMode)) MemoryTrimMode = MemoryTrimMode.PressureAware;
        if (!Enum.IsDefined(CpuPlacement)) CpuPlacement = CpuPlacementSetting.Auto;
        if (TestedPlacement is CpuPlacementMode tested && !Enum.IsDefined(tested)) TestedPlacement = null;
        TestedPlacementTopology ??= "";
        TestedPlacementSummary ??= "";
        TrimTriggerMBPerClient = Math.Clamp(TrimTriggerMBPerClient, 128, 32768);
        TrimIntervalSeconds = Math.Clamp(TrimIntervalSeconds, 1, 300);
        TrimCooldownSeconds = Math.Clamp(TrimCooldownSeconds, 1, 3600);
        MemoryPressureStartPercent = Math.Clamp(MemoryPressureStartPercent, 50, 99);
        MemoryPressureStopPercent = Math.Clamp(MemoryPressureStopPercent, 25, MemoryPressureStartPercent - 1);
        CriticalAvailableMemoryMB = Math.Clamp(CriticalAvailableMemoryMB, 512, 32768);
        // Never High/RealTime (can starve input, audio and the OS) and never Idle (starves the game's network thread).
        ActiveClientPriority = ClampClientPriority(ActiveClientPriority, ProcessPriorityClass.AboveNormal);
        BackgroundClientPriority = ClampClientPriority(BackgroundClientPriority, ProcessPriorityClass.Normal);
        TargetFps = Math.Clamp(TargetFps, 15, 360);

        ManualMainClientIds = (ManualMainClientIds ?? []).Where(id => id > 0).Distinct().ToList();
        MainClientRules = (MainClientRules ?? [])
            .Where(rule => rule is not null && !string.IsNullOrWhiteSpace(NormalizeClientName(rule.ClientName)))
            // Rules saved from a temporary "Potato Launcher — account — Loading/Client running" window title never
            // identify a character again; drop them.
            .Where(rule => !NormalizeClientName(rule.ClientName).StartsWith("Potato Launcher —", StringComparison.Ordinal))
            .GroupBy(rule => NormalizeClientName(rule.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(rule => Math.Max(1, rule.Priority)).First())
            .OrderBy(rule => Math.Max(1, rule.Priority))
            .ThenBy(rule => NormalizeClientName(rule.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select((rule, index) => new MainClientRule { ClientName = NormalizeClientName(rule.ClientName), Priority = index + 1 })
            .ToList();
    }

    internal static string NormalizeClientName(string clientName)
    {
        return string.Join(' ', (clientName ?? string.Empty)
            .Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }
}

internal sealed record OptimizerClientSnapshot(
    int ProcessId,
    string ClientName,
    string WindowTitle,
    bool IsMain,
    bool IsMainCandidate,
    double CpuPercent,
    double? GpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    int ThreadCount,
    int HandleCount,
    DateTime? LastTrimUtc,
    double? Fps = null,
    string Role = "",
    short? EngineFrameLimit = null,
    string Cores = "",
    bool HeldByPotato = false);

internal sealed record SystemMetricsSnapshot(
    double CpuPercent,
    double? GpuPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    bool MemoryPressureActive,
    long CommitLimitBytes = 0,
    long CommitAvailableBytes = 0);

internal sealed class OptimizerAlertEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

internal sealed class IntegratedOptimizerService : IDisposable
{
    private static readonly string[] FfxivProcessNames = ["ffxiv_dx11", "ffxiv"];
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Dictionary<int, ProcessCpuSample> cpuSamples = [];
    private readonly Dictionary<int, DateTime> lastTrimByClientId = [];
    private readonly Dictionary<int, DateTime> unresponsiveSinceByClientId = [];
    private readonly Dictionary<int, (DateTime Start, ClientPolicyState State)> appliedPolicies = [];
    private readonly ExternalGameState frameReader = new();
    private readonly ClientFpsTracker fpsTracker = new();
    private readonly MinimizedClientLimiter minimizedLimiter = new();
    private readonly InputHostWatchdog inputHostWatchdog = new();
    private readonly Dictionary<int, double> latestFps = [];
    private readonly Dictionary<int, short> latestEngineLimit = [];
    private int? lastForegroundClientId;
    private readonly HashSet<int> unresponsiveNotificationsSent = [];
    private readonly GpuUsageSampler gpuSampler = new();
    private readonly SystemUsageSampler systemSampler = new();
    private DateTime lastTrimSweepUtc = DateTime.MinValue;
    private bool memoryPressureActive;
    private readonly Dictionary<int, (DateTime Start, long Mask)> placedMasks = [];
    private int? stickyMainId;
    private int? livePlacementMainId;
    private readonly HashSet<int> runawayClientIds = [];
    private readonly Dictionary<int, (DateTime At, TimeSpan Cpu)> placementLoad = [];
    private readonly Dictionary<int, double> clientLoad = [];
    private double? followerLoad;
    private string lastPlacementSignature = "";
    private PlacementTest? placementTest;
    private readonly Queue<DateTime> externalPriorityChanges = new();
    private readonly Queue<DateTime> externalAffinityChanges = new();

    public OptimizerSettings Settings { get; }
    public event EventHandler? Updated;
    public event EventHandler<OptimizerAlertEventArgs>? Alert;
    public event EventHandler<OptimizerAlertEventArgs>? PlacementTestFinished;

    public IntegratedOptimizerService(OptimizerSettings settings)
    {
        Settings = settings;
        timer.Interval = 1000;
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    public IReadOnlyList<OptimizerClientSnapshot> GetSnapshots()
    {
        var clients = GetFfxivClients();
        try
        {
            var mainSelection = GetMainClientSelection(clients);
            var gpuUsage = gpuSampler.GetUsageByProcessId(clients.Select(client => client.Id));
            return clients.Select(client => CreateSnapshot(client, mainSelection, gpuUsage)).ToList();
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    public void TrimNow()
    {
        var clients = GetFfxivClients();
        try
        {
            TrimWorkingSets(clients, GetMainClientSelection(clients).ActiveMainClientIds, force: true);
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    public void SetMainClient(int processId, string clientName, bool isMain)
    {
        Settings.ManualMainClientIds.RemoveAll(id => id == processId);
        Settings.SetMainCandidate(clientName, isMain);
        Settings.Save();
    }

    public void SetMainPriority(string clientName, int priority)
    {
        Settings.SetMainPriority(clientName, priority);
        Settings.Save();
    }

    public void SaveSettings()
    {
        Settings.Save();
    }

    public string GpuStatusText => gpuSampler.IsAvailable
        ? "GPU counters active"
        : string.IsNullOrWhiteSpace(gpuSampler.LastError) ? "GPU counters unavailable" : gpuSampler.LastError;

    public SystemMetricsSnapshot GetSystemMetrics()
    {
        var snapshot = systemSampler.GetSnapshot(gpuSampler.GetTotalUsage());
        return snapshot with { MemoryPressureActive = memoryPressureActive };
    }

    private void Tick()
    {
        var clients = GetFfxivClients();
        try
        {
            RemoveDeadClientSelections(clients);
            MigrateLegacyMainSelections(clients);
            var mainSelection = GetMainClientSelection(clients);
            UpdateUnresponsiveClients(clients, mainSelection.ActiveMainClientIds);
            UpdateMemoryPressure();

            var clientIds = clients.Select(client => client.Id).ToList();
            var foreground = ClientPolicyNative.ForegroundProcessId();
            lastForegroundClientId = foreground is int pid && clientIds.Contains(pid) ? pid : null;
            SampleFrameRates(clients);
            var activeClientIds = ClientPolicy.ActiveClientIds(
                clientIds,
                mainSelection.CandidateClientIds,
                foreground,
                Settings.FollowForegroundClient);
            // Before 1.0.123 the else below belonged to the TextInputHost watchdog, so turning that off released
            // every brake a moment after it was set.
            if (Settings.ClientPolicyEnabled && Settings.LimitMinimizedClients) LimitRunawayClients(clients, activeClientIds);
            else
            {
                minimizedLimiter.Dispose();
                runawayClientIds.Clear();
            }
            if (Settings.ResetSpinningInputHost && clients.Count > 0)
            {
                var watchdogMessage = inputHostWatchdog.Tick(DateTime.UtcNow);
                if (watchdogMessage.Length > 0) LogDecision(watchdogMessage);
            }
            if (Settings.ClientPolicyEnabled)
            {
                ApplyClientPolicy(clients, activeClientIds);
            }
            ApplyPlacement(clients, mainSelection, lastForegroundClientId);

            if (Settings.WorkingSetTrimEnabled)
            {
                // Never trim the client being played or a configured main client.
                TrimWorkingSets(clients, mainSelection.ActiveMainClientIds.Union(activeClientIds).ToHashSet());
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    private void ApplyClientPolicy(IReadOnlyList<Process> clients, HashSet<int> activeClientIds)
    {
        foreach (var client in clients)
        {
            var start = SafeStartTime(client);
            var role = activeClientIds.Contains(client.Id) ? ClientRole.Active : ClientRole.Background;
            var desired = ClientPolicy.Desired(role, Settings);
            var changed = !appliedPolicies.TryGetValue(client.Id, out var applied) || applied.Start != start || applied.State != desired;
            // Priority already set by Potato but found different: another program changed it (Process Lasso's ProBalance).
            if (ClientPolicyNative.Apply(client, desired, changed) && !changed) externalPriorityChanges.Enqueue(DateTime.UtcNow);
            if (changed)
            {
                appliedPolicies[client.Id] = (start, desired);
                LogDecision($"Client policy: PID {client.Id} {role} priority={desired.Priority} lowMemoryPriority={desired.LowMemoryPriority} preventThrottling={desired.PreventThrottling}");
            }
        }

        var alive = clients.Select(client => client.Id).ToHashSet();
        foreach (var gone in appliedPolicies.Keys.Where(id => !alive.Contains(id)).ToList()) appliedPolicies.Remove(gone);
    }

    public void SetClientPolicyEnabled(bool enabled)
    {
        Settings.ClientPolicyEnabled = enabled;
        Settings.Save();
        if (!enabled) RestoreClientPolicies();
    }

    public CpuTopology Topology => CpuTopology.Current;
    // A stored test result counts only for this CPU layout and this scoring version.
    private string MeasuredKey => $"{Topology.Signature}#v{PlacementTest.ScoringVersion}";
    public bool PlacementTestRunning => placementTest is { Done: false };
    public CpuPlacementMode EffectivePlacement => placementTest is { Done: false, CurrentMode: CpuPlacementMode testing }
        ? testing
        : Settings.CpuPlacement switch
        {
            CpuPlacementSetting.Off => CpuPlacementMode.Off,
            CpuPlacementSetting.ReserveMain => CpuPlacementMode.ReserveMain,
            CpuPlacementSetting.Lanes => CpuPlacementMode.Lanes,
            CpuPlacementSetting.CacheCcdForMain => CpuPlacementMode.CacheCcdForMain,
            _ => Settings.TestedPlacement is CpuPlacementMode tested && Settings.TestedPlacementTopology == MeasuredKey
                ? tested
                : CpuPlacementPlanner.DefaultFor(Topology)
        };

    public string PlacementStatus
    {
        get
        {
            if (placementTest is { Done: false } running) return running.Status;
            var measured = Settings.TestedPlacement.HasValue && Settings.TestedPlacementTopology == MeasuredKey;
            var source = Settings.CpuPlacement != CpuPlacementSetting.Auto ? "chosen by you"
                : measured ? "measured best on this PC"
                : Topology.HasUnequalCores ? "default for unequal cores; run Test placements" : "default for equal cores; run Test placements";
            var text = $"CPU: {Topology.Describe()}.  Placement: {PlacementTest.Label(EffectivePlacement)} ({source}).";
            if (measured && Settings.TestedPlacementSummary.Length > 0) text += Environment.NewLine + "Last test: " + Settings.TestedPlacementSummary;
            return text;
        }
    }

    /// <summary>Times another program changed a client's priority or affinity in the last minute (e.g. Process Lasso).</summary>
    public (int Priority, int Affinity) ExternalChangesLastMinute
    {
        get
        {
            Prune(externalPriorityChanges);
            Prune(externalAffinityChanges);
            return (externalPriorityChanges.Count, externalAffinityChanges.Count);
        }
    }

    private static void Prune(Queue<DateTime> events)
    {
        while (events.Count > 0 && DateTime.UtcNow - events.Peek() > TimeSpan.FromMinutes(1)) events.Dequeue();
    }

    /// <summary>Starts the placement test; returns why it cannot start, or null.</summary>
    public string? StartPlacementTest()
    {
        if (PlacementTestRunning) return "A placement test is already running.";
        if (!Topology.IsSupported) return "This CPU's core layout cannot be read, so placement is not available.";
        if (latestFps.Count < 2) return "Start at least two clients and log them in first: Potato needs to read their FPS.";
        // Runaway clients saturate the CPU and make every placement look the same; fix them first.
        var runaway = latestEngineLimit.Where(entry => entry.Value == 0 && (runawayClientIds.Contains(entry.Key)
            || latestFps.TryGetValue(entry.Key, out var fps) && fps > Settings.TargetFps + RunawayPolicy.RunawayMargin)).Count();
        if (runaway > 0)
            return $"{runaway} client{(runaway == 1 ? " has" : "s have")} no in-game frame limit and would run far above {Settings.TargetFps} without Potato holding {(runaway == 1 ? "it" : "them")}. " +
                   $"Set System Configuration → Display Settings → Frame Rate → {Settings.TargetFps} fps in {(runaway == 1 ? "it" : "them")} (Cap column shows \"game {Settings.TargetFps}\"), then run the test.";
        // Same main as live placement (configured main first), so the test measures what Auto will then apply.
        var main = livePlacementMainId ?? stickyMainId ?? lastForegroundClientId;
        List<CpuPlacementMode> modes = [CpuPlacementMode.Off, CpuPlacementMode.ReserveMain, CpuPlacementMode.Lanes];
        if (Topology.Domains.Count > 1) modes.Add(CpuPlacementMode.CacheCcdForMain);
        placementTest = new PlacementTest(modes, rounds: 2, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), Settings.TargetFps, main);
        LogDecision($"Placement test started (main PID {main?.ToString() ?? "none"}).");
        return null;
    }

    public void StopPlacementTest()
    {
        if (placementTest is not { Done: false } running) return;
        running.Cancel("Placement test stopped.");
        FinishPlacementTest(running);
    }

    private void FinishPlacementTest(PlacementTest test)
    {
        placementTest = null;
        if (test.Result is { } result)
        {
            Settings.TestedPlacement = result.Winner;
            Settings.TestedPlacementTopology = MeasuredKey;
            Settings.TestedPlacementSummary = result.Summary;
            Settings.Save();
        }
        LogDecision(test.Status);
        PlacementTestFinished?.Invoke(this, new OptimizerAlertEventArgs(test.Status));
    }

    // Pins clients per the effective placement. Only processes Potato pinned are touched again, so switching placement
    // off hands each client back to Windows exactly once and never undoes another tool's settings on its own.
    private void ApplyPlacement(IReadOnlyList<Process> clients, MainClientSelection mainSelection, int? foregroundClientId)
    {
        var alive = clients.Select(client => client.Id).ToHashSet();
        if (foregroundClientId is int foreground) stickyMainId = foreground;
        if (stickyMainId is int sticky && !alive.Contains(sticky)) stickyMainId = null;
        if (placementTest is { } test)
        {
            test.Tick(DateTime.UtcNow, clients.Select(client => new ClientRef(client.Id, SafeStartTime(client).ToUniversalTime())).ToList());
            if (test.Done) FinishPlacementTest(test);
        }

        // The main is your configured main client if one runs, otherwise the FFXIV window you used last.
        livePlacementMainId = mainSelection.CandidateClientIds.Count > 0 ? mainSelection.ActiveMainClientIds.FirstOrDefault() : stickyMainId;
        var mainId = placementTest is { Done: false } running ? running.MainId : livePlacementMainId;
        var topology = Topology;
        var mode = EffectivePlacement;
        UpdateFollowerLoad(clients, mainId);
        // Size the main's cores from measured load only: a guess here would re-pin everyone a second later.
        if (mode != CpuPlacementMode.Off && followerLoad is null && clients.Count > 1) return;
        var plan = CpuPlacementPlanner.Plan(topology, mode, clients.Select(client => client.Id).ToList(), mainId, followerLoad);
        foreach (var client in clients)
        {
            var desired = plan.GetValueOrDefault(client.Id, topology.AllMask);
            var start = SafeStartTime(client);
            var known = placedMasks.TryGetValue(client.Id, out var placed) && placed.Start == start;
            if (!known && desired == topology.AllMask) continue;
            var actual = SafeAffinity(client);
            if (known && placed.Mask == desired && actual == desired) continue;
            if (known && placed.Mask == desired && actual is not null) externalAffinityChanges.Enqueue(DateTime.UtcNow);
            try { client.ProcessorAffinity = new IntPtr(desired); } catch { continue; }
            if (desired == topology.AllMask) placedMasks.Remove(client.Id);
            else placedMasks[client.Id] = (start, desired);
        }
        foreach (var gone in placedMasks.Keys.Where(id => !alive.Contains(id)).ToList()) placedMasks.Remove(gone);

        var signature = $"{mode} main={mainId}: " + string.Join(" ", placedMasks.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={CpuTopology.FormatMask(entry.Value.Mask)}"));
        if (signature != lastPlacementSignature)
        {
            lastPlacementSignature = signature;
            LogDecision("Placement " + signature);
        }
    }

    // Followers' CPU use in logical processors, smoothed over ~20 s; sizes the main's reserved cores.
    private void UpdateFollowerLoad(IReadOnlyList<Process> clients, int? mainId)
    {
        var now = DateTime.UtcNow;
        foreach (var client in clients)
        {
            TimeSpan cpu;
            try { cpu = client.TotalProcessorTime; } catch { continue; }
            if (placementLoad.TryGetValue(client.Id, out var last) && now > last.At)
            {
                var logical = Math.Max(0, (cpu - last.Cpu).TotalSeconds / (now - last.At).TotalSeconds);
                clientLoad[client.Id] = clientLoad.TryGetValue(client.Id, out var smoothed) ? smoothed + (logical - smoothed) * 0.05 : logical;
            }
            placementLoad[client.Id] = (now, cpu);
        }
        var alive = clients.Select(client => client.Id).ToHashSet();
        foreach (var gone in placementLoad.Keys.Where(id => !alive.Contains(id)).ToList()) { placementLoad.Remove(gone); clientLoad.Remove(gone); }
        var followers = clients.Where(client => client.Id != mainId).Select(client => client.Id).ToList();
        followerLoad = followers.Count > 0 && followers.All(clientLoad.ContainsKey) ? followers.Sum(id => clientLoad[id]) : null;
    }

    private void RestorePlacement()
    {
        if (placedMasks.Count == 0) return;
        var clients = GetFfxivClients();
        try
        {
            foreach (var client in clients.Where(client => placedMasks.ContainsKey(client.Id)))
            {
                try { client.ProcessorAffinity = new IntPtr(Topology.AllMask); } catch { }
            }
            placedMasks.Clear();
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    private static long? SafeAffinity(Process process)
    {
        try { return process.HasExited ? null : process.ProcessorAffinity.ToInt64(); } catch { return null; }
    }

    // Holds clients nothing else paces: minimized ones, and any client without an in-game frame limit that runs away
    // above the target while it is not the one being played (render-cut or covered windows present no frames, so the
    // NVIDIA cap cannot hold them). The game's own limit, once set, always takes over.
    private void LimitRunawayClients(IReadOnlyList<Process> clients, IReadOnlySet<int> activeClientIds)
    {
        foreach (var client in clients)
        {
            DateTime start;
            try { start = client.StartTime.ToUniversalTime(); } catch { continue; }
            var fps = latestFps.TryGetValue(client.Id, out var measured) ? measured : (double?)null;
            var hasLimit = HasEngineFrameLimit(client.Id);
            var wasRunaway = runawayClientIds.Contains(client.Id);
            var runaway = RunawayPolicy.IsRunaway(wasRunaway, activeClientIds.Contains(client.Id), hasLimit, fps, Settings.TargetFps);
            if (runaway != wasRunaway)
            {
                if (runaway) runawayClientIds.Add(client.Id); else runawayClientIds.Remove(client.Id);
                LogDecision(runaway
                    ? $"Holding PID {client.Id} ({ResolveClientName(client)}) at {Settings.TargetFps}: no in-game frame limit, running at {fps:0} FPS."
                    : $"Released PID {client.Id}: {(hasLimit ? "in-game frame limit set" : "now the active client")}.");
            }
            minimizedLimiter.Update(client.Id, start, RunawayPolicy.ShouldBrake(ClientPolicyNative.IsMinimized(client), runaway, hasLimit), fps, Settings.TargetFps);
        }
        var alive = clients.Select(client => client.Id).ToList();
        minimizedLimiter.Forget(alive);
        runawayClientIds.IntersectWith(alive);
    }

    private void RestoreClientPolicies()
    {
        minimizedLimiter.Dispose();
        runawayClientIds.Clear();
        if (appliedPolicies.Count == 0) return;
        var clients = GetFfxivClients();
        try
        {
            foreach (var client in clients.Where(client => appliedPolicies.ContainsKey(client.Id)))
            {
                ClientPolicyNative.Restore(client);
            }
            appliedPolicies.Clear();
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    private IReadOnlyList<Process> GetFfxivClients()
    {
        var clients = new List<Process>();
        var seen = new HashSet<int>();
        foreach (var name in FfxivProcessNames)
        foreach (var process in Process.GetProcessesByName(name))
        {
            if (IsProcessAlive(process) && seen.Add(process.Id)) clients.Add(process);
            else process.Dispose();
        }
        return clients.OrderBy(SafeStartTime).ThenBy(client => client.Id).ToList();
    }

    private MainClientSelection GetMainClientSelection(IReadOnlyList<Process> clients)
    {
        return MainClientSelector.Select(clients.Select(client => new MainClientIdentity(
            client.Id,
            ResolveClientName(client),
            SafeStartTime(client))).ToList(), Settings);
    }

    private void TrimWorkingSets(IReadOnlyList<Process> clients, IReadOnlySet<int> mainClientIds, bool force = false)
    {
        if (!Settings.WorkingSetTrimEnabled && !force) return;
        if (!force && Settings.MemoryTrimMode == MemoryTrimMode.PressureAware && !memoryPressureActive) return;
        if (!force && (DateTime.UtcNow - lastTrimSweepUtc).TotalSeconds < Settings.TrimIntervalSeconds) return;

        lastTrimSweepUtc = DateTime.UtcNow;
        var now = DateTime.UtcNow;
        var liveIds = clients.Select(client => client.Id).ToHashSet();
        foreach (var staleId in lastTrimByClientId.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            lastTrimByClientId.Remove(staleId);
        }

        var eligibleClients = clients
            .Where(client => !mainClientIds.Contains(client.Id))
            .OrderByDescending(SafeWorkingSet64)
            .ToList();
        foreach (var client in eligibleClients)
        {
            if (!force && SafeWorkingSet64(client) / 1024 / 1024 < Settings.TrimTriggerMBPerClient) continue;
            if (!force &&
                lastTrimByClientId.TryGetValue(client.Id, out var lastTrim) &&
                (now - lastTrim).TotalSeconds < Settings.TrimCooldownSeconds)
            {
                continue;
            }

            if (NativeMethods.TryEmptyWorkingSet(client.Id))
            {
                lastTrimByClientId[client.Id] = now;
                if (!force) break;
            }
        }
    }

    private OptimizerClientSnapshot CreateSnapshot(Process client, MainClientSelection mainSelection, IReadOnlyDictionary<int, double> gpuUsage)
    {
        var title = SafeMainWindowTitle(client);
        var cpuPercent = GetCpuPercent(client);
        gpuUsage.TryGetValue(client.Id, out var gpuPercent);
        return new OptimizerClientSnapshot(
            client.Id,
            ResolveClientName(client),
            title,
            mainSelection.ActiveMainClientIds.Contains(client.Id),
            mainSelection.CandidateClientIds.Contains(client.Id),
            cpuPercent,
            gpuUsage.ContainsKey(client.Id) ? gpuPercent : null,
            SafeWorkingSet64(client),
            SafePrivateMemorySize64(client),
            SafeThreadCount(client),
            SafeHandleCount(client),
            lastTrimByClientId.TryGetValue(client.Id, out var lastTrimUtc) ? lastTrimUtc : null,
            latestFps.TryGetValue(client.Id, out var fps) ? fps : null,
            client.Id == lastForegroundClientId ? "Playing" : mainSelection.CandidateClientIds.Contains(client.Id) ? "Main" : "Background",
            latestEngineLimit.TryGetValue(client.Id, out var engineLimit) ? engineLimit : null,
            placedMasks.TryGetValue(client.Id, out var placed) ? CpuTopology.FormatMask(placed.Mask) : "all",
            minimizedLimiter.IsCapped(client.Id));
    }

    public CapacityEstimate EstimateCapacity(IReadOnlyList<OptimizerClientSnapshot> snapshots, SystemMetricsSnapshot system) =>
        CapacityPlanner.Estimate(
            snapshots.Select(snapshot => new CapacityClientSample(snapshot.CpuPercent, snapshot.WorkingSetBytes, snapshot.PrivateBytes, snapshot.Fps)).ToList(),
            system.CpuPercent, system.AvailableMemoryBytes, system.CommitAvailableBytes, Settings.TargetFps);

    private void SampleFrameRates(IReadOnlyList<Process> clients)
    {
        foreach (var client in clients)
        {
            DateTime start;
            try { start = client.StartTime.ToUniversalTime(); } catch { continue; }
            var sample = frameReader.ReadFrame(client.Id, start);
            var fps = fpsTracker.Update(client.Id, sample);
            if (fps.HasValue) latestFps[client.Id] = fps.Value;
            else latestFps.Remove(client.Id);
            if (sample?.EngineFrameLimit is short limit) latestEngineLimit[client.Id] = limit;
            else latestEngineLimit.Remove(client.Id);
        }
        var alive = clients.Select(client => client.Id).ToList();
        fpsTracker.Forget(alive);
        foreach (var gone in latestFps.Keys.Except(alive).ToList()) latestFps.Remove(gone);
        foreach (var gone in latestEngineLimit.Keys.Except(alive).ToList()) latestEngineLimit.Remove(gone);
    }

    // The game's own limiter holds while minimized; only clients without one need the CPU-cap governor.
    private bool HasEngineFrameLimit(int processId) => latestEngineLimit.TryGetValue(processId, out var limit) && limit > 0;

    private void MigrateLegacyMainSelections(IReadOnlyList<Process> clients)
    {
        if (Settings.ManualMainClientIds.Count == 0) return;
        var liveById = clients.ToDictionary(client => client.Id);
        foreach (var processId in Settings.ManualMainClientIds.ToList())
        {
            if (!liveById.TryGetValue(processId, out var client)) continue;
            Settings.SetMainCandidate(ResolveClientName(client), true);
        }
        Settings.ManualMainClientIds.Clear();
        Settings.Save();
    }

    // A follower whose window stays hung for a minute gets one notification; the main client is the user's own screen.
    private void UpdateUnresponsiveClients(IReadOnlyList<Process> clients, IReadOnlySet<int> mainClientIds)
    {
        var now = DateTime.UtcNow;
        var liveIds = clients.Select(client => client.Id).ToHashSet();
        foreach (var staleId in unresponsiveSinceByClientId.Keys.Where(id => !liveIds.Contains(id)).ToList()) unresponsiveSinceByClientId.Remove(staleId);
        unresponsiveNotificationsSent.RemoveWhere(id => !liveIds.Contains(id));
        foreach (var mainId in mainClientIds)
        {
            unresponsiveSinceByClientId.Remove(mainId);
            unresponsiveNotificationsSent.Remove(mainId);
        }
        foreach (var client in clients.Where(client => !mainClientIds.Contains(client.Id)))
        {
            if (SafeResponding(client))
            {
                unresponsiveSinceByClientId.Remove(client.Id);
                unresponsiveNotificationsSent.Remove(client.Id);
                continue;
            }
            if (!unresponsiveSinceByClientId.TryGetValue(client.Id, out var since))
            {
                unresponsiveSinceByClientId[client.Id] = now;
                continue;
            }
            if ((now - since).TotalSeconds >= 60 && unresponsiveNotificationsSent.Add(client.Id))
            {
                var message = $"{ResolveClientName(client)} has been unresponsive for a minute.";
                LogDecision(message);
                Alert?.Invoke(this, new OptimizerAlertEventArgs(message));
            }
        }
    }

    private void UpdateMemoryPressure()
    {
        var memory = NativeMethods.GetMemoryStatus();
        if (memory.TotalPhysical == 0) return;
        var usedPercent = (memory.TotalPhysical - memory.AvailablePhysical) * 100d / memory.TotalPhysical;
        var availableMb = memory.AvailablePhysical / 1024d / 1024d;
        var wasActive = memoryPressureActive;
        memoryPressureActive = MemoryPressurePolicy.Evaluate(memoryPressureActive, usedPercent, availableMb, Settings);
        if (wasActive != memoryPressureActive)
        {
            LogDecision($"Memory pressure {(memoryPressureActive ? "entered" : "cleared")}: used={usedPercent:0.0}%, available={availableMb:0} MB.");
        }
    }

    private static void LogDecision(string message)
    {
        try
        {
            var path = Path.Combine(MainForm.PersistentDataRoot(), "optimizer-decisions.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private double GetCpuPercent(Process process)
    {
        try
        {
            if (process.HasExited) return 0;
            var now = DateTime.UtcNow;
            var processorTime = process.TotalProcessorTime;
            if (!cpuSamples.TryGetValue(process.Id, out var previous))
            {
                cpuSamples[process.Id] = new ProcessCpuSample(now, processorTime);
                return 0;
            }

            cpuSamples[process.Id] = new ProcessCpuSample(now, processorTime);
            var elapsedMs = Math.Max(1, (now - previous.SampledUtc).TotalMilliseconds);
            var cpuMs = Math.Max(0, (processorTime - previous.TotalProcessorTime).TotalMilliseconds);
            return Math.Round(Math.Min(100, cpuMs / elapsedMs / Math.Max(1, Environment.ProcessorCount) * 100), 1);
        }
        catch
        {
            return 0;
        }
    }

    private void RemoveDeadClientSelections(IReadOnlyList<Process> clients)
    {
        var liveIds = clients.Select(client => client.Id).ToHashSet();
        var before = Settings.ManualMainClientIds.Count;
        Settings.ManualMainClientIds.RemoveAll(id => !liveIds.Contains(id));
        foreach (var staleId in cpuSamples.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            cpuSamples.Remove(staleId);
        }

        if (before != Settings.ManualMainClientIds.Count) Settings.Save();
    }

    // Stable name for main-client rules: the confirmed Character@World for this process when known (it survives
    // loading screens, unlike the window title), otherwise the name from the window title.
    internal static string ResolveClientName(Process client)
    {
        try
        {
            var identity = ClientIdentities.Get(client.Id, client.StartTime.ToUniversalTime());
            if (!string.IsNullOrWhiteSpace(identity)) return OptimizerSettings.NormalizeClientName(identity);
        }
        catch { }
        return ExtractCharacterName(SafeMainWindowTitle(client));
    }

    internal static string ExtractCharacterName(string title)
    {
        var value = OptimizerSettings.NormalizeClientName(title);
        if (string.IsNullOrWhiteSpace(value)) return "Untitled FFXIV client";
        var prefixes = new[] { "FINAL FANTASY XIV - ", "FINAL FANTASY XIV: ", "FFXIV - ", "FFXIV: " };
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return value;
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Dispose();
        systemSampler.Dispose();
        gpuSampler.Dispose();
        placementTest = null;
        RestorePlacement();
        RestoreClientPolicies();
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes) process.Dispose();
    }

    private static DateTime SafeStartTime(Process process)
    {
        try { return process.StartTime; } catch { return DateTime.MaxValue; }
    }

    private static bool IsProcessAlive(Process process)
    {
        try { return !process.HasExited; } catch { return false; }
    }

    private static string SafeMainWindowTitle(Process process)
    {
        try { return process.HasExited ? string.Empty : process.MainWindowTitle; } catch { return string.Empty; }
    }

    private static long SafeWorkingSet64(Process process)
    {
        try { return process.HasExited ? 0 : process.WorkingSet64; } catch { return 0; }
    }

    private static long SafePrivateMemorySize64(Process process)
    {
        try { return process.HasExited ? 0 : process.PrivateMemorySize64; } catch { return 0; }
    }

    private static int SafeThreadCount(Process process)
    {
        try { return process.HasExited ? 0 : process.Threads.Count; } catch { return 0; }
    }

    private static int SafeHandleCount(Process process)
    {
        try { return process.HasExited ? 0 : process.HandleCount; } catch { return 0; }
    }

    private static bool SafeResponding(Process process)
    {
        try
        {
            if (process.HasExited) return true;
            var window = process.MainWindowHandle;
            return window == IntPtr.Zero || !NativeMethods.IsWindowHung(window);
        }
        catch
        {
            return true;
        }
    }

    private sealed record ProcessCpuSample(DateTime SampledUtc, TimeSpan TotalProcessorTime);
}

internal sealed class SystemUsageSampler : IDisposable
{
    private readonly PerformanceCounter? cpuCounter;

    public SystemUsageSampler()
    {
        try
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _ = cpuCounter.NextValue();
        }
        catch
        {
            cpuCounter?.Dispose();
            cpuCounter = null;
        }
    }

    public SystemMetricsSnapshot GetSnapshot(double? gpuPercent)
    {
        var memory = NativeMethods.GetMemoryStatus();
        return new SystemMetricsSnapshot(
            GetCpuPercent(),
            gpuPercent,
            Math.Max(0, (long)(memory.TotalPhysical - memory.AvailablePhysical)),
            Math.Max(0, (long)memory.TotalPhysical),
            Math.Max(0, (long)memory.AvailablePhysical),
            false,
            Math.Max(0, (long)memory.CommitLimit),
            Math.Max(0, (long)memory.CommitAvailable));
    }

    private double GetCpuPercent()
    {
        try
        {
            return cpuCounter is null ? 0 : Math.Round(Math.Clamp(cpuCounter.NextValue(), 0, 100), 1);
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        cpuCounter?.Dispose();
    }
}

internal static class NativeMethods
{
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHungAppWindow(IntPtr windowHandle);

    public static bool TryEmptyWorkingSet(int processId)
    {
        if (processId <= 0) return false;

        IntPtr processHandle = IntPtr.Zero;
        try
        {
            processHandle = OpenProcess(ProcessSetQuota | ProcessQueryLimitedInformation, false, processId);
            return processHandle != IntPtr.Zero && EmptyWorkingSet(processHandle);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (processHandle != IntPtr.Zero) CloseHandle(processHandle);
        }
    }

    public static MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryStatus(status.TotalPhysical, status.AvailablePhysical, status.TotalPageFile, status.AvailablePageFile)
            : new MemoryStatus(0, 0, 0, 0);
    }

    public static bool IsWindowHung(IntPtr windowHandle)
    {
        try { return windowHandle != IntPtr.Zero && IsHungAppWindow(windowHandle); } catch { return false; }
    }

    // TotalPageFile/AvailablePageFile are the system commit limit/available commit (RAM + pagefile).
    public readonly record struct MemoryStatus(ulong TotalPhysical, ulong AvailablePhysical, ulong CommitLimit, ulong CommitAvailable);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

internal static class WildcardMatcher
{
    public static bool IsMatch(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern)) return false;
        return pattern.EndsWith('*')
            ? value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class GpuUsageSampler : IDisposable
{
    private readonly Dictionary<string, PerformanceCounter> countersByInstance = [];
    private DateTime lastRefreshUtc = DateTime.MinValue;
    private double? lastTotalUsage;

    public bool IsAvailable { get; private set; }
    public string LastError { get; private set; } = "";

    private IReadOnlyDictionary<int, double> cachedUsage = new Dictionary<int, double>();
    private DateTime cachedUsageUtc = DateTime.MinValue;
    private HashSet<int> cachedFor = [];

    public IReadOnlyDictionary<int, double> GetUsageByProcessId(IEnumerable<int> processIds)
    {
        var wanted = processIds.ToHashSet();
        // Per-client GPU % is display-only; sampling it more than every 3 s costs more CPU than it is worth.
        if (DateTime.UtcNow - cachedUsageUtc < TimeSpan.FromSeconds(3) && wanted.SetEquals(cachedFor)) return cachedUsage;
        cachedUsage = Sample(wanted);
        cachedUsageUtc = DateTime.UtcNow;
        cachedFor = wanted;
        return cachedUsage;
    }

    private IReadOnlyDictionary<int, double> Sample(HashSet<int> wanted)
    {
        try
        {
            RefreshCountersIfNeeded(wanted);
            var usage = new Dictionary<int, double>();
            var total = 0d;
            foreach (var (instance, counter) in countersByInstance.ToList())
            {
                float value;
                try
                {
                    value = Math.Max(0, counter.NextValue());
                }
                catch (InvalidOperationException)
                {
                    // The process behind this GPU instance exited; drop only this counter.
                    counter.Dispose();
                    countersByInstance.Remove(instance);
                    continue;
                }
                total += value;
                var processId = TryParseGpuEngineProcessId(instance);
                if (processId is null || !wanted.Contains(processId.Value)) continue;
                usage[processId.Value] = usage.GetValueOrDefault(processId.Value) + value;
            }

            IsAvailable = countersByInstance.Count > 0;
            LastError = IsAvailable ? "GPU counters active" : "GPU counters unavailable";
            lastTotalUsage = IsAvailable ? Math.Round(Math.Min(100, total), 1) : null;
            return usage.ToDictionary(entry => entry.Key, entry => Math.Round(Math.Min(100, entry.Value), 1));
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LastError = $"GPU counters unavailable: {ex.Message}";
            lastTotalUsage = null;
            return new Dictionary<int, double>();
        }
    }

    public double? GetTotalUsage()
    {
        // Prefer the driver's own figure; the 3D-engine counters miss DLSS and similar work.
        if (NvidiaGpuUsage.GetUtilizationPercent() is double driverPercent) return driverPercent;
        if (lastTotalUsage.HasValue) return lastTotalUsage.Value;
        _ = GetUsageByProcessId([]);
        return lastTotalUsage;
    }

    private void RefreshCountersIfNeeded(HashSet<int> wanted)
    {
        if ((DateTime.UtcNow - lastRefreshUtc).TotalSeconds < 10) return;
        lastRefreshUtc = DateTime.UtcNow;

        // With the driver providing the system total (NVML), only the game clients' engines are needed: that is a
        // handful of counters instead of one per GPU engine of every process on the PC.
        var clientsOnly = NvidiaGpuUsage.GetUtilizationPercent().HasValue && wanted.Count > 0;
        var category = new PerformanceCounterCategory("GPU Engine");
        var instances = category.GetInstanceNames()
            .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            .Where(name => !clientsOnly || TryParseGpuEngineProcessId(name) is int pid && wanted.Contains(pid))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stale in countersByInstance.Keys.Where(key => !instances.Contains(key)).ToList())
        {
            countersByInstance[stale].Dispose();
            countersByInstance.Remove(stale);
        }

        foreach (var instance in instances)
        {
            if (countersByInstance.ContainsKey(instance)) continue;
            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
            try
            {
                _ = counter.NextValue();
                countersByInstance[instance] = counter;
            }
            catch (InvalidOperationException)
            {
                // Instance vanished between listing and reading; skip it instead of failing the whole refresh.
                counter.Dispose();
            }
        }
    }

    private static int? TryParseGpuEngineProcessId(string instance)
    {
        var marker = "pid_";
        var start = instance.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += marker.Length;
        var end = start;
        while (end < instance.Length && char.IsDigit(instance[end])) end++;
        return int.TryParse(instance[start..end], out var processId) ? processId : null;
    }

    public void Dispose()
    {
        foreach (var counter in countersByInstance.Values) counter.Dispose();
        countersByInstance.Clear();
    }
}
