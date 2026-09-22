using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PotatoLauncher;

/// <summary>Release only FFXIV's two exact instance-count mutexes. Never patches game code.</summary>
internal static class GameClientLimit
{
    internal static bool IsInstanceMutex(string type, string name, int sessionId)
    {
        if (type != "Mutant") return false;
        var root = $"\\Sessions\\{sessionId}\\BaseNamedObjects\\";
        if (name.StartsWith(root, StringComparison.Ordinal)) name = name[root.Length..];
        else if (name.StartsWith("\\BaseNamedObjects\\", StringComparison.Ordinal)) name = name[18..];
        else return false;
        return name is "6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00" or
            "6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game01";
    }

    internal static int InspectOrRelease(int processId, DateTime startTimeUtc, bool release)
    {
        using var process = Process.GetProcessById(processId);
        using var self = Process.GetCurrentProcess();
        if (process.ProcessName != "ffxiv_dx11" || process.StartTime.ToUniversalTime() != startTimeUtc ||
            process.SessionId != self.SessionId || process.HasExited)
            throw new InvalidOperationException("Refusing to inspect an unverified game process.");
        using var remote = OpenProcess(0x0400 | 0x0040 | 0x1000, false, processId);
        if (remote.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot access FFXIV instance mutexes.");
        GameProcessIdentity.Verify(remote, startTimeUtc);
        var matches = 0;
        foreach (var value in Snapshot(remote))
        {
            if (!DuplicateHandle(remote, value, GetCurrentProcess(), out var copy, 0, false, 2)) continue;
            using (copy)
            {
                // Never query file/pipe names: those can hang inside NtQueryObject.
                var type = Query(copy, 2);
                if (type != "Mutant") continue;
                var name = Query(copy, 1);
                if (!IsInstanceMutex(type, name, process.SessionId)) continue;
                if (!release) { matches++; continue; }
                if (process.HasExited || process.StartTime.ToUniversalTime() != startTimeUtc)
                    throw new InvalidOperationException("Game exited before releasing its instance mutex.");
                // Re-duplicate and revalidate immediately before closing the source handle.
                if (!DuplicateHandle(remote, value, GetCurrentProcess(), out var check, 0, false, 2)) continue;
                using (check)
                {
                    if (Query(check, 2) != "Mutant" || Query(check, 1) != name) continue;
                    if (!DuplicateHandle(remote, value, GetCurrentProcess(), out var removed, 0, false, 3))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not release the FFXIV instance mutex.");
                    removed.Dispose();
                    matches++;
                }
            }
        }
        return matches;
    }

    private static List<IntPtr> Snapshot(SafeProcessHandle process)
    {
        for (var size = 32768; size <= 16 * 1024 * 1024; size *= 2)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQueryInformationProcess(process, 51, buffer, size, out _);
                if (status == unchecked((int)0xC0000004)) continue;
                if (status < 0) throw new IOException($"FFXIV handle enumeration failed (0x{status:X8}).");
                var count = Marshal.ReadInt64(buffer);
                // This application and structure layout are x64 only.
                if (count < 0 || count > (size - 16) / 40) throw new IOException("Invalid handle snapshot.");
                var handles = new List<IntPtr>();
                for (var i = 0; i < count; i++) handles.Add(Marshal.ReadIntPtr(buffer, 16 + i * 40));
                return handles;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("FFXIV handle snapshot exceeded the size limit.");
    }

    private static string Query(SafeFileHandle handle, int informationClass)
    {
        const int size = 4096;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryObject(handle, informationClass, buffer, size, out _) < 0) return "";
            var length = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, 8);
            if (length == 0 || length % 2 != 0 || text.ToInt64() < buffer.ToInt64() ||
                text.ToInt64() + length > buffer.ToInt64() + size) return "";
            return Marshal.PtrToStringUni(text, length / 2) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(SafeProcessHandle source, IntPtr value, IntPtr target,
        out SafeFileHandle copy, uint access, bool inherit, uint options);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, IntPtr buffer, int size, out int required);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(SafeFileHandle handle, int informationClass, IntPtr buffer, int size, out int required);
}
