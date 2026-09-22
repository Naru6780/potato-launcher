using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PotatoLauncher;

internal static class GameProcessIdentity
{
    // Validate the opened handle, not just a PID that could have been reused before OpenProcess.
    internal static void Verify(SafeProcessHandle handle, DateTime expectedStartUtc)
    {
        if (!GetProcessTimes(handle, out var created, out _, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (DateTime.FromFileTimeUtc(created) != expectedStartUtc)
            throw new InvalidOperationException("Opened game process identity changed.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation,
        out long exit, out long kernel, out long user);
}
