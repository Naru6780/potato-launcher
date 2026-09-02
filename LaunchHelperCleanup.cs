using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

internal sealed record LaunchHelperProcess(int ProcessId, string ProcessName, DateTime StartTimeUtc);
internal sealed record LaunchHelperCleanupScope(IReadOnlySet<int> ProcessIdsBeforeLaunch, int? DirectLauncherProcessId);

internal sealed record LaunchHelperCleanupResult(int StoppedCount, IReadOnlyList<string> Failures);

internal static class LaunchHelperCleanup
{
    private static readonly string[] ProcessNames = ["XIVLauncher", "DalamudCrashHandler", "Dalamud.Injector"];
    private static readonly HashSet<string> AllowedProcessNames = new(ProcessNames, StringComparer.OrdinalIgnoreCase);

    public static HashSet<int> CaptureProcessIds()
    {
        return CaptureProcesses().Select(process => process.ProcessId).ToHashSet();
    }

    public static IReadOnlyList<LaunchHelperProcess> CaptureOwnedProcesses(LaunchHelperCleanupScope scope, int gameClientProcessId)
    {
        return SelectLaunchProcesses(
            scope.ProcessIdsBeforeLaunch,
            CaptureProcesses(),
            scope.DirectLauncherProcessId,
            gameClientProcessId,
            CaptureParentProcessIds());
    }

    internal static IReadOnlyList<LaunchHelperProcess> SelectLaunchProcesses(
        IReadOnlySet<int> processIdsBeforeLaunch,
        IEnumerable<LaunchHelperProcess> currentProcesses,
        int? directlyStartedProcessId,
        int gameClientProcessId,
        IReadOnlyDictionary<int, int> parentProcessIds)
    {
        return currentProcesses
            .Where(process => AllowedProcessNames.Contains(process.ProcessName))
            .Where(process => !processIdsBeforeLaunch.Contains(process.ProcessId) || process.ProcessId == directlyStartedProcessId)
            .Where(process => IsOwnedByLaunch(process, directlyStartedProcessId, gameClientProcessId, parentProcessIds))
            .GroupBy(process => process.ProcessId)
            .Select(group => group.First())
            .OrderBy(process => process.StartTimeUtc)
            .ThenBy(process => process.ProcessId)
            .ToList();
    }

    private static bool IsOwnedByLaunch(
        LaunchHelperProcess process,
        int? directlyStartedProcessId,
        int gameClientProcessId,
        IReadOnlyDictionary<int, int> parentProcessIds)
    {
        if (process.ProcessName.Equals("XIVLauncher", StringComparison.OrdinalIgnoreCase))
        {
            return process.ProcessId == directlyStartedProcessId ||
                   directlyStartedProcessId.HasValue && IsDescendantOf(process.ProcessId, directlyStartedProcessId.Value, parentProcessIds);
        }

        return IsDescendantOf(process.ProcessId, gameClientProcessId, parentProcessIds) ||
               directlyStartedProcessId.HasValue && IsDescendantOf(process.ProcessId, directlyStartedProcessId.Value, parentProcessIds);
    }

    internal static bool IsDescendantOf(int processId, int ancestorProcessId, IReadOnlyDictionary<int, int> parentProcessIds)
    {
        var visited = new HashSet<int>();
        var current = processId;
        while (visited.Add(current) && parentProcessIds.TryGetValue(current, out var parent) && parent > 0)
        {
            if (parent == ancestorProcessId) return true;
            current = parent;
        }
        return false;
    }

    public static async Task<LaunchHelperCleanupResult> StopProcessesAsync(IReadOnlyList<LaunchHelperProcess> targets)
    {
        return await Task.Run(() =>
        {
            var stopped = 0;
            var failures = new List<string>();
            foreach (var target in targets)
            {
                try
                {
                    using var process = Process.GetProcessById(target.ProcessId);
                    if (process.HasExited || !AllowedProcessNames.Contains(process.ProcessName)) continue;
                    var currentStartTimeUtc = SafeStartTimeUtc(process);
                    if (target.StartTimeUtc == DateTime.MinValue || currentStartTimeUtc == DateTime.MinValue || currentStartTimeUtc != target.StartTimeUtc) continue;

                    process.Kill(entireProcessTree: false);
                    if (process.WaitForExit(2000))
                    {
                        stopped++;
                    }
                    else
                    {
                        failures.Add($"{target.ProcessName} (PID {target.ProcessId}) did not exit.");
                    }
                }
                catch (ArgumentException)
                {
                    // It already exited after the snapshot.
                }
                catch (Exception exception)
                {
                    failures.Add($"{target.ProcessName} (PID {target.ProcessId}): {exception.Message}");
                }
            }

            return new LaunchHelperCleanupResult(stopped, failures);
        });
    }

    private static IReadOnlyList<LaunchHelperProcess> CaptureProcesses()
    {
        var captured = new List<LaunchHelperProcess>();
        foreach (var processName in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        var startTimeUtc = SafeStartTimeUtc(process);
                        if (startTimeUtc != DateTime.MinValue)
                        {
                            captured.Add(new LaunchHelperProcess(process.Id, process.ProcessName, startTimeUtc));
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return captured;
    }

    private static IReadOnlyDictionary<int, int> CaptureParentProcessIds()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue) return parents;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return parents;
            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return parents;
    }

    private static DateTime SafeStartTimeUtc(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); } catch { return DateTime.MinValue; }
    }

    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }
}
