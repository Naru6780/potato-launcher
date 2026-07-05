using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotatoLauncher;

internal enum CpuAssignmentMode
{
    SplitLanes,
    AllAvailableCores,
    OnePhysicalCorePerClient
}

internal sealed class OptimizerSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<int> AllowedMainLogicalProcessors { get; } = [2, 4, 6, 8, 10, 12];
    public static IReadOnlyList<int> AllowedFollowerLogicalProcessors { get; } = [1, 2, 4, 6, 8];

    public bool OptimizerEnabled { get; set; }
    public bool CpuAffinityOptimizationEnabled { get; set; }
    public bool WorkingSetTrimEnabled { get; set; } = true;
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

    [JsonIgnore]
    public string[] DefaultMainClientTitlePatterns { get; } =
    [
        "Artemis Potato*",
        "Garrison Mangler*",
        "Kazuko Aura*"
    ];

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
        var normalizedValue = Math.Clamp(logicalProcessors, 0, Math.Max(0, Environment.ProcessorCount - 1));
        if (normalizedValue == 0)
        {
            MainReservedLogicalProcessorsByName.Remove(key);
            return;
        }

        MainReservedLogicalProcessorsByName[key] = normalizedValue;
    }

    public void Normalize()
    {
        if (!Enum.IsDefined(CpuAssignmentMode)) CpuAssignmentMode = CpuAssignmentMode.SplitLanes;
        MainLogicalProcessors = NearestAllowed(MainLogicalProcessors, AllowedMainLogicalProcessors, 6);
        FollowerLogicalProcessors = NearestAllowed(FollowerLogicalProcessors, AllowedFollowerLogicalProcessors, 4);
        SystemReservedLogicalProcessors = Math.Clamp(SystemReservedLogicalProcessors, 0, Math.Max(0, Environment.ProcessorCount - 1));
        TrimTriggerMBPerClient = Math.Clamp(TrimTriggerMBPerClient, 128, 32768);
        TrimIntervalSeconds = Math.Clamp(TrimIntervalSeconds, 1, 300);
        TrimCooldownSeconds = Math.Clamp(TrimCooldownSeconds, 1, 3600);
        CpuLaneIntervalSeconds = Math.Clamp(CpuLaneIntervalSeconds, 1, 300);

        ManualMainClientIds = (ManualMainClientIds ?? []).Where(id => id > 0).Distinct().ToList();
        var normalizedMainReservations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in MainReservedLogicalProcessorsByName ?? new Dictionary<string, int>())
        {
            var key = NormalizeClientName(entry.Key);
            var value = Math.Clamp(entry.Value, 0, Math.Max(0, Environment.ProcessorCount - 1));
            if (!string.IsNullOrWhiteSpace(key) && value > 0)
            {
                normalizedMainReservations[key] = value;
            }
        }

        MainReservedLogicalProcessorsByName = normalizedMainReservations;
    }

    private static int NearestAllowed(int value, IReadOnlyList<int> allowed, int fallback)
    {
        return allowed.Contains(value)
            ? value
            : allowed.OrderBy(candidate => Math.Abs(candidate - value)).FirstOrDefault(fallback);
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
    double CpuPercent,
    double? GpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    int ThreadCount,
    int HandleCount,
    long? AffinityMask,
    DateTime? LastTrimUtc);

internal sealed record SystemMetricsSnapshot(
    double CpuPercent,
    double? GpuPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes);

internal sealed class IntegratedOptimizerService : IDisposable
{
    private static readonly string[] FfxivProcessNames = ["ffxiv_dx11", "ffxiv"];
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Dictionary<int, ProcessCpuSample> cpuSamples = [];
    private readonly Dictionary<int, DateTime> lastTrimByClientId = [];
    private readonly GpuUsageSampler gpuSampler = new();
    private readonly SystemUsageSampler systemSampler = new();
    private DateTime lastCpuLaneUtc = DateTime.MinValue;
    private DateTime lastTrimSweepUtc = DateTime.MinValue;
    private bool appliedClientScheduling;

    public OptimizerSettings Settings { get; }
    public event EventHandler? Updated;

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
            var mainClientIds = GetMainClientIds(clients);
            var gpuUsage = gpuSampler.GetUsageByProcessId(clients.Select(client => client.Id));
            return clients.Select(client => CreateSnapshot(client, mainClientIds, gpuUsage)).ToList();
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
            TrimWorkingSets(clients, force: true);
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

    public void SetMainClient(int processId, bool isMain)
    {
        Settings.ManualMainClientIds.RemoveAll(id => id == processId);
        if (isMain) Settings.ManualMainClientIds.Add(processId);
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
        return systemSampler.GetSnapshot(gpuSampler.GetTotalUsage());
    }

    private void Tick()
    {
        var clients = GetFfxivClients();
        try
        {
            RemoveDeadClientSelections(clients);
            if (Settings.OptimizerEnabled)
            {
                ApplyCpuLanes(clients);
            }

            if (Settings.WorkingSetTrimEnabled)
            {
                TrimWorkingSets(clients);
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

    private HashSet<int> GetMainClientIds(IReadOnlyList<Process> clients)
    {
        var liveClientIds = clients.Select(client => client.Id).ToHashSet();
        var mainIds = Settings.ManualMainClientIds.Where(liveClientIds.Contains).ToHashSet();
        foreach (var client in clients)
        {
            if (mainIds.Contains(client.Id)) continue;
            if (Settings.DefaultMainClientTitlePatterns.Any(pattern => WildcardMatcher.IsMatch(SafeMainWindowTitle(client), pattern)))
            {
                mainIds.Add(client.Id);
            }
        }

        if (mainIds.Count == 0 && clients.Count > 0) mainIds.Add(clients[0].Id);
        return mainIds;
    }

    private void ApplyCpuLanes(IReadOnlyList<Process> clients, bool force = false)
    {
        if (!Settings.OptimizerEnabled && !force) return;
        if (!Settings.CpuAffinityOptimizationEnabled && !force) return;
        if (!force && (DateTime.UtcNow - lastCpuLaneUtc).TotalSeconds < Settings.CpuLaneIntervalSeconds) return;

        lastCpuLaneUtc = DateTime.UtcNow;
        var mainClientIds = GetMainClientIds(clients);
        var allocator = new CpuAffinityAllocator(Settings);
        foreach (var assignment in allocator.CreateAssignments(clients, mainClientIds))
        {
            var clientName = ExtractCharacterName(SafeMainWindowTitle(assignment.Process));
            ProcessScheduling.TryApplyAffinity(assignment.Process, assignment.AffinityMask);
        }

        ResetLauncherScheduling();
        appliedClientScheduling = true;
    }

    private void TrimWorkingSets(IReadOnlyList<Process> clients, bool force = false)
    {
        if (!Settings.WorkingSetTrimEnabled && !force) return;
        if (!force && (DateTime.UtcNow - lastTrimSweepUtc).TotalSeconds < Settings.TrimIntervalSeconds) return;

        lastTrimSweepUtc = DateTime.UtcNow;
        var now = DateTime.UtcNow;
        var liveIds = clients.Select(client => client.Id).ToHashSet();
        foreach (var staleId in lastTrimByClientId.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            lastTrimByClientId.Remove(staleId);
        }

        foreach (var client in clients)
        {
            if (!force && SafeWorkingSet64(client) / 1024 / 1024 < Settings.TrimTriggerMBPerClient) continue;
            if (!force &&
                lastTrimByClientId.TryGetValue(client.Id, out var lastTrim) &&
                (now - lastTrim).TotalSeconds < Settings.TrimCooldownSeconds)
            {
                continue;
            }

            if (NativeMethods.TryEmptyWorkingSet(client.Handle))
            {
                lastTrimByClientId[client.Id] = now;
            }
        }
    }

    private OptimizerClientSnapshot CreateSnapshot(Process client, IReadOnlySet<int> mainClientIds, IReadOnlyDictionary<int, double> gpuUsage)
    {
        var title = SafeMainWindowTitle(client);
        var cpuPercent = GetCpuPercent(client);
        gpuUsage.TryGetValue(client.Id, out var gpuPercent);
        return new OptimizerClientSnapshot(
            client.Id,
            ExtractCharacterName(title),
            title,
            mainClientIds.Contains(client.Id),
            cpuPercent,
            gpuUsage.ContainsKey(client.Id) ? gpuPercent : null,
            SafeWorkingSet64(client),
            SafePrivateMemorySize64(client),
            SafeThreadCount(client),
            SafeHandleCount(client),
            SafeAffinityMask(client),
            lastTrimByClientId.TryGetValue(client.Id, out var lastTrimUtc) ? lastTrimUtc : null);
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
            Math.Max(0, (long)memory.TotalPhysical));
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
            for (var index = 0; index < 62; index++)
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
}

internal static class ProcessorTopology
{
    private const int RelationProcessorCore = 0;

    public static IReadOnlyList<long> GetPhysicalCoreMasks(int logicalProcessorCount)
    {
        var reportedMasks = TryGetWindowsPhysicalCoreMasks(logicalProcessorCount);
        return reportedMasks.Count > 0 ? reportedMasks : CreateFallbackSiblingPairs(logicalProcessorCount);
    }

    public static IReadOnlyList<long> CreateFallbackSiblingPairs(int logicalProcessorCount)
    {
        var masks = new List<long>();
        var maxLogicalProcessor = Math.Min(logicalProcessorCount, 62);
        for (var index = 0; index < maxLogicalProcessor; index += 2)
        {
            var width = Math.Min(2, maxLogicalProcessor - index);
            masks.Add(ProcessorAffinity.CreateMask(index, width, logicalProcessorCount));
        }

        return masks.Count == 0 ? [1L] : masks;
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
                if (mask <= 0) continue;
                mask &= ProcessorAffinity.CreateMask(0, logicalProcessorCount, logicalProcessorCount);
                if (mask > 0) masks.Add(mask);
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
        for (var index = 0; index < 62; index++)
        {
            if ((mask & (1L << index)) != 0) return index;
        }

        return int.MaxValue;
    }

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
    public static long CreateMask(int startIndex, int count, int logicalProcessorCount)
    {
        long mask = 0;
        var lastIndex = Math.Min(startIndex + count - 1, Math.Min(logicalProcessorCount - 1, 61));
        for (var index = startIndex; index <= lastIndex; index++)
        {
            if (index >= 0) mask |= 1L << index;
        }

        return mask == 0 ? 1 : mask;
    }

    public static int CountSetBits(long mask)
    {
        var count = 0;
        for (var index = 0; index < 62; index++)
        {
            if ((mask & (1L << index)) != 0) count++;
        }

        return count;
    }

    public static string FormatMask(long mask)
    {
        var cpuIndices = new List<int>();
        var maxIndex = Math.Min(Environment.ProcessorCount - 1, 61);
        for (var index = 0; index <= maxIndex; index++)
        {
            if ((mask & (1L << index)) != 0) cpuIndices.Add(index);
        }

        return cpuIndices.Count == 0 ? "none" : string.Join(",", cpuIndices);
    }
}

internal static class NativeMethods
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static bool TryEmptyWorkingSet(IntPtr processHandle)
    {
        try { return EmptyWorkingSet(processHandle); } catch { return false; }
    }

    public static MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryStatus(status.TotalPhysical, status.AvailablePhysical)
            : new MemoryStatus(0, 0);
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
