using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

// Keeps every FFXIV client at its frame cap with as little contention as possible:
//  - the client you are playing (foreground) and your configured main clients are "active";
//  - every other client is "background".
// Nothing here lowers a client's frame rate: the goal is that all clients hold their cap (e.g. 60 FPS).
internal enum ClientRole
{
    Active,
    Background
}

// Minimized clients are handled by MinimizedClientLimiter (hard CPU cap). Windows' timer/EcoQoS throttling was measured
// to have no effect on a minimized FFXIV's loop rate, so it is not used for that.
internal sealed record ClientPolicyState(ProcessPriorityClass Priority, bool LowMemoryPriority, bool PreventThrottling);

internal static class ClientPolicy
{
    public static ClientPolicyState Desired(ClientRole role, OptimizerSettings settings) => role == ClientRole.Active
        ? new ClientPolicyState(settings.ActiveClientPriority, LowMemoryPriority: false, settings.PreventWindowsThrottling)
        : new ClientPolicyState(settings.BackgroundClientPriority, settings.LowMemoryPriorityForBackground, settings.PreventWindowsThrottling);

    public static HashSet<int> ActiveClientIds(IReadOnlyCollection<int> clientIds, IEnumerable<int> mainCandidateIds, int? foregroundProcessId, bool followForeground)
    {
        var active = mainCandidateIds.Where(clientIds.Contains).ToHashSet();
        if (followForeground && foregroundProcessId is int foreground && clientIds.Contains(foreground)) active.Add(foreground);
        return active;
    }
}

// Win32 side. Every call is best effort: a client that exits or refuses access is simply skipped.
internal static class ClientPolicyNative
{
    private const int ProcessMemoryPriority = 0;
    private const int ProcessPowerThrottling = 4;
    private const uint ProcessSetInformation = 0x0200;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint ThrottleExecutionSpeed = 0x1;
    private const uint ThrottleIgnoreTimerResolution = 0x4;
    internal const uint MemoryPriorityNormal = 5;
    internal const uint MemoryPriorityLow = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    // Priority is cheap to check, so it is re-asserted every tick (other tools such as Process Lasso's ProBalance may
    // change it). Memory priority and throttling are only written when the desired state changes.
    // Returns true when the priority had to be corrected.
    public static bool Apply(Process process, ClientPolicyState state, bool stateChanged)
    {
        var corrected = TrySetPriority(process, state.Priority);
        if (!stateChanged) return corrected;
        TrySetMemoryPriority(process.Id, state.LowMemoryPriority ? MemoryPriorityLow : MemoryPriorityNormal);
        TrySetThrottling(process.Id, preventThrottling: state.PreventThrottling);
        // GPU scheduling follows the CPU role: the client being played gets its frames scheduled first on the GPU.
        TrySetGpuPriority(process.Id, state.Priority == ProcessPriorityClass.AboveNormal ? GpuPriorityAboveNormal : GpuPriorityNormal);
        return corrected;
    }

    public static void Restore(Process process)
    {
        TrySetPriority(process, ProcessPriorityClass.Normal);
        TrySetMemoryPriority(process.Id, MemoryPriorityNormal);
        TrySetThrottling(process.Id, preventThrottling: false);
        TrySetGpuPriority(process.Id, GpuPriorityNormal);
    }

    // D3DKMT_SCHEDULINGPRIORITYCLASS: 2 = NORMAL, 3 = ABOVE_NORMAL (HIGH/REALTIME are never used).
    private const int GpuPriorityNormal = 2;
    private const int GpuPriorityAboveNormal = 3;

    private static void TrySetGpuPriority(int processId, int priorityClass)
    {
        WithHandle(processId, handle => D3DKMTSetProcessSchedulingPriorityClass(handle, priorityClass));
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priorityClass);

    private static bool TrySetPriority(Process process, ProcessPriorityClass priority)
    {
        try
        {
            if (process.HasExited || process.PriorityClass == priority) return false;
            process.PriorityClass = priority;
            return true;
        }
        catch { return false; }
    }

    private static void TrySetMemoryPriority(int processId, uint priority)
    {
        WithHandle(processId, handle =>
        {
            var value = priority;
            SetProcessInformation(handle, ProcessMemoryPriority, ref value, sizeof(uint));
        });
    }

    // preventThrottling = true: tell Windows never to apply EcoQoS or timer-resolution throttling to this client.
    // Windows 11 otherwise ignores the timer resolution of minimized/covered windows, which breaks the game's
    // frame limiter and drops covered clients below their cap. false: hand the decision back to Windows.
    private static void TrySetThrottling(int processId, bool preventThrottling)
    {
        WithHandle(processId, handle =>
        {
            var state = new PowerThrottlingState
            {
                Version = PowerThrottlingCurrentVersion,
                ControlMask = preventThrottling ? ThrottleExecutionSpeed | ThrottleIgnoreTimerResolution : 0,

                StateMask = 0
            };
            SetProcessInformation(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
        });
    }

    private static void WithHandle(int processId, Action<IntPtr> action)
    {
        var handle = OpenProcess(ProcessSetInformation | ProcessQueryInformation | ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return;
        try { action(handle); }
        catch { }
        finally { CloseHandle(handle); }
    }

    public static bool IsMinimized(Process process)
    {
        try
        {
            var window = process.MainWindowHandle;
            return window != IntPtr.Zero && IsIconic(window);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    public static int? ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var processId);
        return processId == 0 ? null : (int)processId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref uint information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
