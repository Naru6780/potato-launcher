using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotatoLauncher;

internal enum CpuAssignmentMode
{
    SplitLanes,
    AllAvailableCores,
    OnePhysicalCorePerClient,
    AdaptiveSharedPools
}

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

    public static IReadOnlyList<int> AllowedMainLogicalProcessors => GetAllowedLogicalProcessorCounts(Environment.ProcessorCount);
    public static IReadOnlyList<int> AllowedFollowerLogicalProcessors => GetAllowedLogicalProcessorCounts(Environment.ProcessorCount);

    public bool OptimizerEnabled { get; set; }
    public bool CpuAffinityOptimizationEnabled { get; set; }
    public bool WorkingSetTrimEnabled { get; set; } = true;
    public MemoryTrimMode MemoryTrimMode { get; set; } = MemoryTrimMode.PressureAware;
    public bool CpuPreviewOnly { get; set; }
    public int MainLogicalProcessors { get; set; } = 6;
    public int FollowerLogicalProcessors { get; set; } = 4;
    public int SystemReservedLogicalProcessors { get; set; } = 4;
    public int TrimTriggerMBPerClient { get; set; } = 1024;
    public int TrimIntervalSeconds { get; set; } = 10;
    public int TrimCooldownSeconds { get; set; } = 30;
    public int CpuLaneIntervalSeconds { get; set; } = 5;
    public CpuAssignmentMode CpuAssignmentMode { get; set; } = CpuAssignmentMode.SplitLanes;
    public List<int> ManualMainClientIds { get; set; } = [];
    public Dictionary<string, int> MainReservedLogicalProcessorsByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MainClientRule> MainClientRules { get; set; } = [];
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
            var settings = JsonSerializer.Deserialize<OptimizerSettings>(json, JsonOptions) ?? new OptimizerSettings();
            MigrateLegacyCpuOptimizationFlag(settings, json);
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new OptimizerSettings();
        }
    }

    private static void MigrateLegacyCpuOptimizationFlag(OptimizerSettings settings, string json)
    {
        if (settings.CpuAffinityOptimizationEnabled) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("cpuPriorityManagementEnabled", out var legacyValue) &&
                legacyValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                settings.CpuAffinityOptimizationEnabled = legacyValue.GetBoolean();
            }
        }
        catch
        {
        }
    }

    public void Save()
    {
        Normalize();
        var path = MainForm.OptimizerSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public int GetMainReservedLogicalProcessors(string clientName)
    {
        var key = NormalizeClientName(clientName);
        return MainReservedLogicalProcessorsByName.TryGetValue(key, out var value) ? value : 0;
    }

    public void SetMainReservedLogicalProcessors(string clientName, int logicalProcessors)
    {
        var key = NormalizeClientName(clientName);
        if (string.IsNullOrWhiteSpace(key)) return;
        var normalizedValue = Math.Clamp(logicalProcessors, 0, ProcessorAffinity.GetSupportedLogicalProcessorCount(Environment.ProcessorCount));
        if (normalizedValue == 0)
        {
            MainReservedLogicalProcessorsByName.Remove(key);
            return;
        }

        MainReservedLogicalProcessorsByName[key] = normalizedValue;
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

    public void Normalize()
    {
        Normalize(Environment.ProcessorCount);
    }

    internal void Normalize(int logicalProcessorCount)
    {
        var supportedLogicalProcessorCount = ProcessorAffinity.GetSupportedLogicalProcessorCount(logicalProcessorCount);
        if (!Enum.IsDefined(CpuAssignmentMode)) CpuAssignmentMode = CpuAssignmentMode.SplitLanes;
        if (!Enum.IsDefined(MemoryTrimMode)) MemoryTrimMode = MemoryTrimMode.PressureAware;
        MainLogicalProcessors = Math.Clamp(MainLogicalProcessors, 1, supportedLogicalProcessorCount);
        FollowerLogicalProcessors = Math.Clamp(FollowerLogicalProcessors, 1, supportedLogicalProcessorCount);
        SystemReservedLogicalProcessors = Math.Clamp(SystemReservedLogicalProcessors, 0, Math.Max(0, supportedLogicalProcessorCount - 1));
        TrimTriggerMBPerClient = Math.Clamp(TrimTriggerMBPerClient, 128, 32768);
        TrimIntervalSeconds = Math.Clamp(TrimIntervalSeconds, 1, 300);
        TrimCooldownSeconds = Math.Clamp(TrimCooldownSeconds, 1, 3600);
        CpuLaneIntervalSeconds = Math.Clamp(CpuLaneIntervalSeconds, 1, 300);
        MemoryPressureStartPercent = Math.Clamp(MemoryPressureStartPercent, 50, 99);
        MemoryPressureStopPercent = Math.Clamp(MemoryPressureStopPercent, 25, MemoryPressureStartPercent - 1);
        CriticalAvailableMemoryMB = Math.Clamp(CriticalAvailableMemoryMB, 512, 32768);

        ManualMainClientIds = (ManualMainClientIds ?? []).Where(id => id > 0).Distinct().ToList();
        var normalizedMainReservations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in MainReservedLogicalProcessorsByName ?? new Dictionary<string, int>())
        {
            var key = NormalizeClientName(entry.Key);
            var value = Math.Clamp(entry.Value, 0, supportedLogicalProcessorCount);
            if (!string.IsNullOrWhiteSpace(key) && value > 0)
            {
                normalizedMainReservations[key] = value;
            }
        }

        MainReservedLogicalProcessorsByName = normalizedMainReservations;

        MainClientRules = (MainClientRules ?? [])
            .Where(rule => rule is not null && !string.IsNullOrWhiteSpace(NormalizeClientName(rule.ClientName)))
            .GroupBy(rule => NormalizeClientName(rule.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(rule => Math.Max(1, rule.Priority)).First())
            .OrderBy(rule => Math.Max(1, rule.Priority))
            .ThenBy(rule => NormalizeClientName(rule.ClientName), StringComparer.OrdinalIgnoreCase)
            .Select((rule, index) => new MainClientRule { ClientName = NormalizeClientName(rule.ClientName), Priority = index + 1 })
            .ToList();
    }

    internal static IReadOnlyList<int> GetAllowedLogicalProcessorCounts(int logicalProcessorCount)
    {
        var supportedLogicalProcessorCount = ProcessorAffinity.GetSupportedLogicalProcessorCount(logicalProcessorCount);
        return Enumerable.Range(1, supportedLogicalProcessorCount).ToArray();
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
    long? AffinityMask,
    long? PlannedAffinityMask,
    bool IsRescued,
    DateTime? LastTrimUtc);

internal sealed record SystemMetricsSnapshot(
    double CpuPercent,
    double? GpuPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    bool MemoryPressureActive);

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
    private readonly Dictionary<int, long> plannedAffinitiesByClientId = [];
    private readonly Dictionary<int, DateTime> unresponsiveSinceByClientId = [];
    private readonly Dictionary<int, DateTime> rescueUntilByClientId = [];
    private readonly HashSet<int> unresponsiveNotificationsSent = [];
    private readonly GpuUsageSampler gpuSampler = new();
    private readonly SystemUsageSampler systemSampler = new();
    private DateTime lastCpuLaneUtc = DateTime.MinValue;
    private DateTime lastTrimSweepUtc = DateTime.MinValue;
    private bool appliedClientScheduling;
    private bool memoryPressureActive;
    private string lastAllocationSignature = "";

    public OptimizerSettings Settings { get; }
    public event EventHandler? Updated;
    public event EventHandler<OptimizerAlertEventArgs>? Alert;

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
            RefreshPlannedAssignments(clients, mainSelection.ActiveMainClientIds);
            var gpuUsage = gpuSampler.GetUsageByProcessId(clients.Select(client => client.Id));
            return clients.Select(client => CreateSnapshot(client, mainSelection, gpuUsage)).ToList();
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    public void ApplyNow()
    {
        var clients = GetFfxivClients();
        try
        {
            ApplyCpuLanes(clients, force: true);
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

    public void RestoreClients()
    {
        var clients = GetFfxivClients();
        try
        {
            var fullMask = ProcessorAffinity.CreateMask(0, Environment.ProcessorCount, Environment.ProcessorCount);
            foreach (var client in clients)
            {
                ProcessScheduling.TryRestore(client, fullMask);
            }

            appliedClientScheduling = false;
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    public void SetOptimizerEnabled(bool enabled)
    {
        Settings.OptimizerEnabled = enabled;
        Settings.Save();
        if (!enabled) RestoreClients();
    }

    public void SetCpuOptimizationEnabled(bool enabled)
    {
        Settings.OptimizerEnabled = enabled;
        Settings.CpuAffinityOptimizationEnabled = enabled;
        Settings.Save();
        if (!enabled) RestoreClients();
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

    public void RescueClient(int processId)
    {
        rescueUntilByClientId[processId] = DateTime.UtcNow.AddSeconds(30);
        lastCpuLaneUtc = DateTime.MinValue;
        LogDecision($"Manual rescue started for PID {processId}.");
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
            UpdateRescueState(clients, mainSelection.ActiveMainClientIds);
            UpdateMemoryPressure();
            if (Settings.OptimizerEnabled)
            {
                ApplyCpuLanes(clients);
            }

            if (Settings.WorkingSetTrimEnabled)
            {
                TrimWorkingSets(clients, mainSelection.ActiveMainClientIds);
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            DisposeProcesses(clients);
        }
    }

    private IReadOnlyList<Process> GetFfxivClients()
    {
        return FfxivProcessNames
            .SelectMany(name => Process.GetProcessesByName(name))
            .Where(IsProcessAlive)
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .OrderBy(SafeStartTime)
            .ThenBy(client => client.Id)
            .ToList();
    }

    private MainClientSelection GetMainClientSelection(IReadOnlyList<Process> clients)
    {
        return MainClientSelector.Select(clients.Select(client => new MainClientIdentity(
            client.Id,
            ExtractCharacterName(SafeMainWindowTitle(client)),
            SafeStartTime(client))).ToList(), Settings);
    }

    private void ApplyCpuLanes(IReadOnlyList<Process> clients, bool force = false)
    {
        if (!Settings.OptimizerEnabled && !force) return;
        if (!Settings.CpuAffinityOptimizationEnabled && !force) return;
        if (!force && (DateTime.UtcNow - lastCpuLaneUtc).TotalSeconds < Settings.CpuLaneIntervalSeconds) return;

        lastCpuLaneUtc = DateTime.UtcNow;
        var mainClientIds = GetMainClientSelection(clients).ActiveMainClientIds;
        var allocator = new CpuAffinityAllocator(Settings);
        var assignments = allocator.CreateAssignments(clients, mainClientIds);
        var followerPoolMask = assignments
            .Where(assignment => !mainClientIds.Contains(assignment.Process.Id))
            .Aggregate(0L, (mask, assignment) => mask | assignment.AffinityMask);
        foreach (var assignment in assignments)
        {
            var plannedMask = rescueUntilByClientId.ContainsKey(assignment.Process.Id) && followerPoolMask != 0
                && !mainClientIds.Contains(assignment.Process.Id)
                ? followerPoolMask
                : assignment.AffinityMask;
            plannedAffinitiesByClientId[assignment.Process.Id] = plannedMask;
            if (!Settings.CpuPreviewOnly)
            {
                ProcessScheduling.TryApplyAffinity(assignment.Process, plannedMask);
            }
        }

        var signature = $"mode={Settings.CpuAssignmentMode};preview={Settings.CpuPreviewOnly};main={string.Join(',', mainClientIds.Order())};" +
                        string.Join(';', plannedAffinitiesByClientId.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}=0x{entry.Value:X}"));
        if (!string.Equals(signature, lastAllocationSignature, StringComparison.Ordinal))
        {
            lastAllocationSignature = signature;
            LogDecision($"Allocation {signature}");
        }

        if (!Settings.CpuPreviewOnly)
        {
            ResetLauncherScheduling();
            appliedClientScheduling = true;
        }
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
            .Where(client => !mainClientIds.Contains(client.Id) && !rescueUntilByClientId.ContainsKey(client.Id))
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
            ExtractCharacterName(title),
            title,
            mainSelection.ActiveMainClientIds.Contains(client.Id),
            mainSelection.CandidateClientIds.Contains(client.Id),
            cpuPercent,
            gpuUsage.ContainsKey(client.Id) ? gpuPercent : null,
            SafeWorkingSet64(client),
            SafePrivateMemorySize64(client),
            SafeThreadCount(client),
            SafeHandleCount(client),
            SafeAffinityMask(client),
            plannedAffinitiesByClientId.GetValueOrDefault(client.Id) is var plannedMask && plannedMask != 0 ? plannedMask : null,
            rescueUntilByClientId.ContainsKey(client.Id),
            lastTrimByClientId.TryGetValue(client.Id, out var lastTrimUtc) ? lastTrimUtc : null);
    }

    private void RefreshPlannedAssignments(IReadOnlyList<Process> clients, IReadOnlySet<int> mainClientIds)
    {
        var assignments = new CpuAffinityAllocator(Settings).CreateAssignments(clients, mainClientIds);
        var followerPoolMask = assignments.Where(assignment => !mainClientIds.Contains(assignment.Process.Id))
            .Aggregate(0L, (mask, assignment) => mask | assignment.AffinityMask);
        foreach (var assignment in assignments)
        {
            plannedAffinitiesByClientId[assignment.Process.Id] = rescueUntilByClientId.ContainsKey(assignment.Process.Id) && followerPoolMask != 0
                && !mainClientIds.Contains(assignment.Process.Id)
                ? followerPoolMask
                : assignment.AffinityMask;
        }
    }

    private void MigrateLegacyMainSelections(IReadOnlyList<Process> clients)
    {
        if (Settings.ManualMainClientIds.Count == 0) return;
        var liveById = clients.ToDictionary(client => client.Id);
        foreach (var processId in Settings.ManualMainClientIds.ToList())
        {
            if (!liveById.TryGetValue(processId, out var client)) continue;
            Settings.SetMainCandidate(ExtractCharacterName(SafeMainWindowTitle(client)), true);
        }
        Settings.ManualMainClientIds.Clear();
        Settings.Save();
    }

    private void UpdateRescueState(IReadOnlyList<Process> clients, IReadOnlySet<int> mainClientIds)
    {
        var now = DateTime.UtcNow;
        var liveIds = clients.Select(client => client.Id).ToHashSet();
        foreach (var staleId in rescueUntilByClientId.Keys.Where(id => !liveIds.Contains(id) || rescueUntilByClientId[id] <= now).ToList()) rescueUntilByClientId.Remove(staleId);
        foreach (var staleId in unresponsiveSinceByClientId.Keys.Where(id => !liveIds.Contains(id)).ToList()) unresponsiveSinceByClientId.Remove(staleId);
        foreach (var mainId in mainClientIds)
        {
            rescueUntilByClientId.Remove(mainId);
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
            if ((now - since).TotalSeconds >= 15 && !rescueUntilByClientId.ContainsKey(client.Id))
            {
                rescueUntilByClientId[client.Id] = now.AddSeconds(30);
                lastCpuLaneUtc = DateTime.MinValue;
                LogDecision($"Automatic rescue started for {ExtractCharacterName(SafeMainWindowTitle(client))} (PID {client.Id}).");
            }
            if ((now - since).TotalSeconds >= 60 && unresponsiveNotificationsSent.Add(client.Id))
            {
                var message = $"{ExtractCharacterName(SafeMainWindowTitle(client))} remains unresponsive after CPU rescue.";
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
        foreach (var staleId in plannedAffinitiesByClientId.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            plannedAffinitiesByClientId.Remove(staleId);
        }

        if (before != Settings.ManualMainClientIds.Count) Settings.Save();
    }

    private static void ResetLauncherScheduling()
    {
        var fullMask = ProcessorAffinity.CreateMask(0, Environment.ProcessorCount, Environment.ProcessorCount);
        foreach (var launcher in Process.GetProcessesByName("XIVLauncher"))
        {
            try
            {
                ProcessScheduling.TryRestore(launcher, fullMask);
            }
            finally
            {
                launcher.Dispose();
            }
        }
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
        if (appliedClientScheduling) RestoreClients();
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

    private static long? SafeAffinityMask(Process process)
    {
        try { return process.HasExited ? null : process.ProcessorAffinity.ToInt64(); } catch { return null; }
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
            false);
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

internal sealed record CpuAffinityAssignment(Process Process, long AffinityMask);
internal sealed record ProcessorCacheDomain(long Mask, long CacheSizeBytes, int Level);

internal sealed class CpuAffinityAllocator
{
    private readonly OptimizerSettings settings;

    public CpuAffinityAllocator(OptimizerSettings settings)
    {
        this.settings = settings;
    }

    public IReadOnlyList<CpuAffinityAssignment> CreateAssignments(IReadOnlyList<Process> clients, IReadOnlySet<int> mainClientIds)
    {
        if (clients.Count == 0) return [];
        var logicalProcessorCount = Environment.ProcessorCount;
        if (settings.CpuAssignmentMode == CpuAssignmentMode.AllAvailableCores)
        {
            var fullMask = ProcessorAffinity.CreateMask(0, logicalProcessorCount, logicalProcessorCount);
            return clients
                .Select(client => new CpuAffinityAssignment(client, fullMask))
                .ToList();
        }

        var physicalCoreMasks = ProcessorTopology.GetPhysicalCoreMasks(logicalProcessorCount);
        if (settings.CpuAssignmentMode == CpuAssignmentMode.AdaptiveSharedPools)
        {
            var orderedClients = clients.OrderBy(SafeStartTime).ThenBy(client => client.Id).ToList();
            var requestedByMainId = orderedClients
                .Where(client => mainClientIds.Contains(client.Id))
                .ToDictionary(client => client.Id, client => GetMainLogicalProcessorReservation(client, settings.MainLogicalProcessors));
            var masks = CreateAdaptiveMasks(
                orderedClients.Select(client => client.Id).ToList(),
                mainClientIds,
                requestedByMainId,
                physicalCoreMasks,
                ProcessorTopology.GetLastLevelCacheDomains(logicalProcessorCount),
                settings.SystemReservedLogicalProcessors);
            return orderedClients.Select(client => new CpuAffinityAssignment(client, masks[client.Id])).ToList();
        }
        if (settings.CpuAssignmentMode == CpuAssignmentMode.OnePhysicalCorePerClient)
        {
            return CreateOnePhysicalCorePerClientAssignments(clients, mainClientIds, physicalCoreMasks);
        }

        var usablePhysicalCoreMasks = GetUsablePhysicalCoreMasks(physicalCoreMasks, settings.SystemReservedLogicalProcessors);
        var usableLogicalProcessors = Math.Max(1, usablePhysicalCoreMasks.Sum(ProcessorAffinity.CountSetBits));
        var mainLogicalProcessors = Math.Min(usableLogicalProcessors, Math.Max(1, settings.MainLogicalProcessors));
        var followerLaneLogicalProcessors = Math.Min(usableLogicalProcessors, Math.Max(1, settings.FollowerLogicalProcessors));

        var mainClients = clients.Where(client => mainClientIds.Contains(client.Id)).OrderBy(SafeStartTime).ThenBy(client => client.Id).ToList();
        var followerClients = clients.Where(client => !mainClientIds.Contains(client.Id)).OrderBy(SafeStartTime).ThenBy(client => client.Id).ToList();
        var mainMasks = CreateMainMasks(mainClients, usablePhysicalCoreMasks, mainLogicalProcessors);
        var reservedMainPhysicalCores = Math.Min(usablePhysicalCoreMasks.Count, mainMasks.Values.Sum(mask => mask.PhysicalCoreCount));
        var followerMasks = CreateFollowerLaneMasks(usablePhysicalCoreMasks, reservedMainPhysicalCores, followerLaneLogicalProcessors);

        var followerMaskByClientId = new Dictionary<int, long>();
        for (var index = 0; index < followerClients.Count; index++)
        {
            followerMaskByClientId[followerClients[index].Id] = followerMasks[index % followerMasks.Count];
        }

        var assignments = new List<CpuAffinityAssignment>(clients.Count);
        foreach (var client in clients)
        {
            assignments.Add(mainMasks.TryGetValue(client.Id, out var lane)
                ? new CpuAffinityAssignment(client, lane.Mask)
                : new CpuAffinityAssignment(client, followerMaskByClientId[client.Id]));
        }

        return assignments;
    }

    internal static IReadOnlyDictionary<int, long> CreateAdaptiveMasks(
        IReadOnlyList<int> clientIds,
        IReadOnlySet<int> mainClientIds,
        IReadOnlyDictionary<int, int> requestedLogicalProcessorsByMainId,
        IReadOnlyList<long> physicalCoreMasks,
        IReadOnlyList<ProcessorCacheDomain> cacheDomains,
        int systemReservedLogicalProcessors)
    {
        if (clientIds.Count == 0) return new Dictionary<int, long>();
        var usableCores = GetUsablePhysicalCoreMasks(physicalCoreMasks, systemReservedLogicalProcessors).ToList();
        if (usableCores.Count == 0) usableCores.Add(1L);
        var domainGroups = CreateDomainGroups(usableCores, cacheDomains);
        var masks = new Dictionary<int, long>();

        foreach (var mainId in clientIds.Where(mainClientIds.Contains))
        {
            var requested = Math.Max(1, requestedLogicalProcessorsByMainId.GetValueOrDefault(mainId, 1));
            var requiredCores = Math.Max(1, GetRequiredPhysicalCoreCount(usableCores, requested));
            var selectedGroup = domainGroups.FirstOrDefault(group => group.Cores.Count >= requiredCores)
                ?? domainGroups.OrderByDescending(group => group.Cores.Count).First();
            var selectedCores = selectedGroup.Cores.Take(Math.Min(requiredCores, selectedGroup.Cores.Count)).ToList();
            if (selectedCores.Count == 0) selectedCores.Add(usableCores[0]);
            masks[mainId] = selectedCores.Aggregate(0L, (mask, core) => mask | core);
            foreach (var core in selectedCores)
            {
                foreach (var group in domainGroups) group.Cores.Remove(core);
            }
        }

        var followerGroups = domainGroups.Where(group => group.Cores.Count > 0).ToList();
        var weightedPools = followerGroups
            .SelectMany(group => Enumerable.Repeat(group.Cores.Aggregate(0L, (mask, core) => mask | core), group.Cores.Count))
            .ToList();
        if (weightedPools.Count == 0)
        {
            var fallback = usableCores.Aggregate(0L, (mask, core) => mask | core);
            weightedPools.Add(fallback == 0 ? 1L : fallback);
        }

        var followerIndex = 0;
        foreach (var followerId in clientIds.Where(id => !mainClientIds.Contains(id)))
        {
            masks[followerId] = weightedPools[followerIndex++ % weightedPools.Count];
        }
        return masks;
    }

    private static List<CacheDomainCoreGroup> CreateDomainGroups(IReadOnlyList<long> cores, IReadOnlyList<ProcessorCacheDomain> cacheDomains)
    {
        var groups = cacheDomains
            .OrderByDescending(domain => domain.CacheSizeBytes)
            .ThenBy(domain => ProcessorTopology.GetLowestSetBitIndexForOrdering(domain.Mask))
            .Select(domain => new CacheDomainCoreGroup(domain, cores.Where(core => (core & domain.Mask) == core).ToList()))
            .Where(group => group.Cores.Count > 0)
            .ToList();
        var assigned = groups.SelectMany(group => group.Cores).ToHashSet();
        var unmatched = cores.Where(core => !assigned.Contains(core)).ToList();
        if (unmatched.Count > 0) groups.Add(new CacheDomainCoreGroup(new ProcessorCacheDomain(unmatched.Aggregate(0L, (mask, core) => mask | core), 0, 0), unmatched));
        if (groups.Count == 0) groups.Add(new CacheDomainCoreGroup(new ProcessorCacheDomain(cores.Aggregate(0L, (mask, core) => mask | core), 0, 0), cores.ToList()));
        return groups;
    }

    private IReadOnlyList<CpuAffinityAssignment> CreateOnePhysicalCorePerClientAssignments(IReadOnlyList<Process> clients, IReadOnlySet<int> mainClientIds, IReadOnlyList<long> physicalCoreMasks)
    {
        if (physicalCoreMasks.Count == 0) physicalCoreMasks = [1L];
        var mainClients = clients.Where(client => mainClientIds.Contains(client.Id)).OrderBy(SafeStartTime).ThenBy(client => client.Id).ToList();
        var followerClients = clients.Where(client => !mainClientIds.Contains(client.Id)).OrderBy(SafeStartTime).ThenBy(client => client.Id).ToList();
        var mainPairMasks = GetMainPhysicalCorePairMasks(mainClients, physicalCoreMasks);
        var followerSlots = GetFollowerPhysicalCoreSlots(physicalCoreMasks, mainPairMasks.Count);

        var followerMaskByClientId = new Dictionary<int, long>();
        for (var index = 0; index < followerClients.Count; index++)
        {
            followerMaskByClientId[followerClients[index].Id] = followerSlots[index % followerSlots.Count];
        }

        return clients
            .Select(client => mainPairMasks.TryGetValue(client.Id, out var mainPairMask)
                ? new CpuAffinityAssignment(client, mainPairMask)
                : new CpuAffinityAssignment(client, followerMaskByClientId[client.Id]))
            .ToList();
    }

    private static Dictionary<int, long> GetMainPhysicalCorePairMasks(IReadOnlyList<Process> mainClients, IReadOnlyList<long> physicalCoreMasks)
    {
        var masks = new Dictionary<int, long>();
        for (var index = 0; index < mainClients.Count; index++)
        {
            masks[mainClients[index].Id] = physicalCoreMasks[index % physicalCoreMasks.Count];
        }

        return masks;
    }

    private static IReadOnlyList<long> GetFollowerPhysicalCoreSlots(IReadOnlyList<long> physicalCoreMasks, int reservedMainPhysicalCores)
    {
        var primaryLogicalProcessorMasks = CreatePrimaryLogicalProcessorMasks(physicalCoreMasks);
        var startIndex = Math.Min(Math.Max(0, reservedMainPhysicalCores), Math.Max(0, primaryLogicalProcessorMasks.Count - 1));
        var slots = primaryLogicalProcessorMasks.Skip(startIndex).ToList();
        return slots.Count > 0 ? slots : primaryLogicalProcessorMasks;
    }

    private static IReadOnlyList<long> CreatePrimaryLogicalProcessorMasks(IReadOnlyList<long> physicalCoreMasks)
    {
        var masks = new List<long>();
        foreach (var physicalCoreMask in physicalCoreMasks)
        {
            for (var index = 0; index < ProcessorAffinity.MaskBitCount; index++)
            {
                var logicalProcessorMask = 1L << index;
                if ((physicalCoreMask & logicalProcessorMask) != 0)
                {
                    masks.Add(logicalProcessorMask);
                    break;
                }
            }
        }

        return masks.Count > 0 ? masks : [1L];
    }

    private Dictionary<int, CpuLane> CreateMainMasks(IReadOnlyList<Process> mainClients, IReadOnlyList<long> usablePhysicalCoreMasks, int defaultLogicalProcessors)
    {
        var lanes = new Dictionary<int, CpuLane>();
        var nextCoreIndex = 0;
        foreach (var client in mainClients)
        {
            var requestedLogicalProcessors = GetMainLogicalProcessorReservation(client, defaultLogicalProcessors);
            var lane = CreateMaskFromPhysicalCores(usablePhysicalCoreMasks, nextCoreIndex, requestedLogicalProcessors);
            if (nextCoreIndex + lane.PhysicalCoreCount <= usablePhysicalCoreMasks.Count)
            {
                lanes[client.Id] = lane;
                nextCoreIndex += lane.PhysicalCoreCount;
                continue;
            }

            lanes[client.Id] = CreateMaskFromPhysicalCores(usablePhysicalCoreMasks, 0, requestedLogicalProcessors);
        }

        return lanes;
    }

    private int GetMainLogicalProcessorReservation(Process client, int fallbackLogicalProcessors)
    {
        var clientName = ExtractProcessClientName(client);
        var requestedLogicalProcessors = settings.GetMainReservedLogicalProcessors(clientName);
        return Math.Min(Math.Max(1, requestedLogicalProcessors > 0 ? requestedLogicalProcessors : fallbackLogicalProcessors), Environment.ProcessorCount);
    }

    private static string ExtractProcessClientName(Process client)
    {
        try
        {
            return IntegratedOptimizerService.ExtractCharacterName(client.MainWindowTitle);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<long> CreateFollowerLaneMasks(IReadOnlyList<long> usablePhysicalCoreMasks, int reservedMainPhysicalCores, int laneLogicalProcessors)
    {
        var masks = new List<long>();
        if (reservedMainPhysicalCores >= usablePhysicalCoreMasks.Count)
        {
            var fallbackStartIndex = Math.Max(0, usablePhysicalCoreMasks.Count - GetRequiredPhysicalCoreCount(usablePhysicalCoreMasks, laneLogicalProcessors));
            masks.Add(CreateMaskFromPhysicalCores(usablePhysicalCoreMasks, fallbackStartIndex, laneLogicalProcessors).Mask);
            return masks;
        }

        for (var index = Math.Max(0, reservedMainPhysicalCores); index < usablePhysicalCoreMasks.Count;)
        {
            var lane = CreateMaskFromPhysicalCores(usablePhysicalCoreMasks, index, laneLogicalProcessors);
            masks.Add(lane.Mask);
            index += lane.PhysicalCoreCount;
        }

        if (masks.Count == 0)
        {
            var fallbackStartIndex = Math.Max(0, usablePhysicalCoreMasks.Count - GetRequiredPhysicalCoreCount(usablePhysicalCoreMasks, laneLogicalProcessors));
            masks.Add(CreateMaskFromPhysicalCores(usablePhysicalCoreMasks, fallbackStartIndex, laneLogicalProcessors).Mask);
        }

        return masks;
    }

    private static IReadOnlyList<long> GetUsablePhysicalCoreMasks(IReadOnlyList<long> physicalCoreMasks, int reservedLogicalProcessors)
    {
        if (physicalCoreMasks.Count == 0) return [1L];
        var masks = physicalCoreMasks.ToList();
        var remainingReservedLogicalProcessors = Math.Max(0, reservedLogicalProcessors);
        while (masks.Count > 1 && remainingReservedLogicalProcessors > 0)
        {
            var lastMask = masks[^1];
            masks.RemoveAt(masks.Count - 1);
            remainingReservedLogicalProcessors -= ProcessorAffinity.CountSetBits(lastMask);
        }

        return masks;
    }

    private static CpuLane CreateMaskFromPhysicalCores(IReadOnlyList<long> physicalCoreMasks, int startCoreIndex, int requestedLogicalProcessors)
    {
        if (physicalCoreMasks.Count == 0) return new CpuLane(1, 1);
        var mask = 0L;
        var physicalCoreCount = 0;
        var logicalProcessorCount = 0;
        for (var index = Math.Max(0, startCoreIndex); index < physicalCoreMasks.Count; index++)
        {
            mask |= physicalCoreMasks[index];
            physicalCoreCount++;
            logicalProcessorCount += ProcessorAffinity.CountSetBits(physicalCoreMasks[index]);
            if (logicalProcessorCount >= requestedLogicalProcessors) break;
        }

        return mask == 0 ? new CpuLane(physicalCoreMasks[0], 1) : new CpuLane(mask, physicalCoreCount);
    }

    private static int GetRequiredPhysicalCoreCount(IReadOnlyList<long> physicalCoreMasks, int requestedLogicalProcessors)
    {
        return CreateMaskFromPhysicalCores(physicalCoreMasks, 0, requestedLogicalProcessors).PhysicalCoreCount;
    }

    private static DateTime SafeStartTime(Process process)
    {
        try { return process.StartTime; } catch { return DateTime.MaxValue; }
    }

    private sealed record CpuLane(long Mask, int PhysicalCoreCount);
    private sealed record CacheDomainCoreGroup(ProcessorCacheDomain Domain, List<long> Cores);
}

internal static class ProcessorTopology
{
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;

    public static IReadOnlyList<long> GetPhysicalCoreMasks(int logicalProcessorCount)
    {
        var reportedMasks = TryGetWindowsPhysicalCoreMasks(logicalProcessorCount);
        return reportedMasks.Count > 0 ? reportedMasks : CreateFallbackSiblingPairs(logicalProcessorCount);
    }

    public static IReadOnlyList<long> CreateFallbackSiblingPairs(int logicalProcessorCount)
    {
        var masks = new List<long>();
        var maxLogicalProcessor = ProcessorAffinity.GetSupportedLogicalProcessorCount(logicalProcessorCount);
        for (var index = 0; index < maxLogicalProcessor; index += 2)
        {
            var width = Math.Min(2, maxLogicalProcessor - index);
            masks.Add(ProcessorAffinity.CreateMask(index, width, logicalProcessorCount));
        }

        return masks.Count == 0 ? [1L] : masks;
    }

    public static IReadOnlyList<ProcessorCacheDomain> GetLastLevelCacheDomains(int logicalProcessorCount)
    {
        var byteLength = 0;
        _ = GetLogicalProcessorInformation(IntPtr.Zero, ref byteLength);
        if (byteLength <= 0) return [];
        var buffer = Marshal.AllocHGlobal(byteLength);
        try
        {
            if (!GetLogicalProcessorInformation(buffer, ref byteLength)) return [];
            var entrySize = Marshal.SizeOf<LogicalProcessorInformation>();
            var entryCount = byteLength / entrySize;
            var unionOffset = IntPtr.Size == 8 ? 16 : 8;
            var domains = new List<ProcessorCacheDomain>();
            for (var index = 0; index < entryCount; index++)
            {
                var pointer = IntPtr.Add(buffer, index * entrySize);
                if (Marshal.ReadInt32(pointer, IntPtr.Size) != RelationCache) continue;
                var level = Marshal.ReadByte(pointer, unionOffset);
                var cacheSize = unchecked((uint)Marshal.ReadInt32(pointer, unionOffset + 4));
                var mask = unchecked((long)(IntPtr.Size == 8 ? Marshal.ReadInt64(pointer) : Marshal.ReadInt32(pointer)));
                mask &= ProcessorAffinity.CreateMask(0, logicalProcessorCount, logicalProcessorCount);
                if (mask != 0 && cacheSize > 0) domains.Add(new ProcessorCacheDomain(mask, cacheSize, level));
            }
            if (domains.Count == 0) return [];
            var highestLevel = domains.Max(domain => domain.Level);
            return domains.Where(domain => domain.Level == highestLevel)
                .GroupBy(domain => domain.Mask)
                .Select(group => group.OrderByDescending(domain => domain.CacheSizeBytes).First())
                .OrderByDescending(domain => domain.CacheSizeBytes)
                .ThenBy(domain => GetLowestSetBitIndex(domain.Mask))
                .ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<long> TryGetWindowsPhysicalCoreMasks(int logicalProcessorCount)
    {
        var byteLength = 0;
        _ = GetLogicalProcessorInformation(IntPtr.Zero, ref byteLength);
        if (byteLength <= 0) return [];

        var buffer = Marshal.AllocHGlobal(byteLength);
        try
        {
            if (!GetLogicalProcessorInformation(buffer, ref byteLength)) return [];
            var entrySize = Marshal.SizeOf<LogicalProcessorInformation>();
            var entryCount = byteLength / entrySize;
            var masks = new List<long>();
            for (var index = 0; index < entryCount; index++)
            {
                var pointer = IntPtr.Add(buffer, index * entrySize);
                var entry = Marshal.PtrToStructure<LogicalProcessorInformation>(pointer);
                if (entry.Relationship != RelationProcessorCore) continue;
                var mask = unchecked((long)entry.ProcessorMask.ToUInt64());
                if (mask == 0) continue;
                mask &= ProcessorAffinity.CreateMask(0, logicalProcessorCount, logicalProcessorCount);
                if (mask != 0) masks.Add(mask);
            }

            return masks.Distinct().OrderBy(GetLowestSetBitIndex).ThenBy(ProcessorAffinity.CountSetBits).ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetLowestSetBitIndex(long mask)
    {
        for (var index = 0; index < ProcessorAffinity.MaskBitCount; index++)
        {
            if ((mask & (1L << index)) != 0) return index;
        }

        return int.MaxValue;
    }

    internal static int GetLowestSetBitIndexForOrdering(long mask) => GetLowestSetBitIndex(mask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref int returnedLength);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LogicalProcessorInformation
    {
        public readonly UIntPtr ProcessorMask;
        public readonly int Relationship;
        private readonly ulong reserved0;
        private readonly ulong reserved1;
    }
}

internal static class ProcessScheduling
{
    public static void TryApplyAffinity(Process process, long affinityMask)
    {
        try
        {
            if (!process.HasExited) process.ProcessorAffinity = new IntPtr(affinityMask);
        }
        catch
        {
        }
    }

    public static void TryRestore(Process process, long affinityMask)
    {
        try
        {
            if (!process.HasExited) process.PriorityClass = ProcessPriorityClass.Normal;
        }
        catch
        {
        }

        TryApplyAffinity(process, affinityMask);
    }
}

internal static class ProcessorAffinity
{
    public const int MaskBitCount = sizeof(long) * 8;

    public static int GetSupportedLogicalProcessorCount(int logicalProcessorCount)
    {
        return Math.Clamp(logicalProcessorCount, 1, MaskBitCount);
    }

    public static string FormatLogicalProcessorCapacity(int logicalProcessorCount)
    {
        var detectedLogicalProcessorCount = Math.Max(1, logicalProcessorCount);
        var supportedLogicalProcessorCount = GetSupportedLogicalProcessorCount(detectedLogicalProcessorCount);
        return detectedLogicalProcessorCount == supportedLogicalProcessorCount
            ? $"{detectedLogicalProcessorCount} logical CPUs"
            : $"{detectedLogicalProcessorCount} logical CPUs detected ({supportedLogicalProcessorCount} affinity-addressable)";
    }

    public static long CreateMask(int startIndex, int count, int logicalProcessorCount)
    {
        long mask = 0;
        var lastIndex = Math.Min(startIndex + count - 1, GetSupportedLogicalProcessorCount(logicalProcessorCount) - 1);
        for (var index = startIndex; index <= lastIndex; index++)
        {
            if (index >= 0) mask |= 1L << index;
        }

        return mask == 0 ? 1 : mask;
    }

    public static int CountSetBits(long mask)
    {
        return BitOperations.PopCount(unchecked((ulong)mask));
    }

    public static string FormatMask(long mask)
    {
        var cpuIndices = new List<int>();
        var maxIndex = GetSupportedLogicalProcessorCount(Environment.ProcessorCount) - 1;
        for (var index = 0; index <= maxIndex; index++)
        {
            if ((mask & (1L << index)) != 0) cpuIndices.Add(index);
        }

        return cpuIndices.Count == 0 ? "none" : string.Join(",", cpuIndices);
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
            ? new MemoryStatus(status.TotalPhysical, status.AvailablePhysical)
            : new MemoryStatus(0, 0);
    }

    public static bool IsWindowHung(IntPtr windowHandle)
    {
        try { return windowHandle != IntPtr.Zero && IsHungAppWindow(windowHandle); } catch { return false; }
    }

    public readonly record struct MemoryStatus(ulong TotalPhysical, ulong AvailablePhysical);

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

    public IReadOnlyDictionary<int, double> GetUsageByProcessId(IEnumerable<int> processIds)
    {
        var wanted = processIds.ToHashSet();

        try
        {
            RefreshCountersIfNeeded();
            var usage = new Dictionary<int, double>();
            var total = 0d;
            foreach (var (instance, counter) in countersByInstance)
            {
                var value = Math.Max(0, counter.NextValue());
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
        if (lastTotalUsage.HasValue) return lastTotalUsage.Value;
        _ = GetUsageByProcessId([]);
        return lastTotalUsage;
    }

    private void RefreshCountersIfNeeded()
    {
        if ((DateTime.UtcNow - lastRefreshUtc).TotalSeconds < 10 && countersByInstance.Count > 0) return;
        lastRefreshUtc = DateTime.UtcNow;

        var category = new PerformanceCounterCategory("GPU Engine");
        var instances = category.GetInstanceNames()
            .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
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
            _ = counter.NextValue();
            countersByInstance[instance] = counter;
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
