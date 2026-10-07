using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

// Last confirmed "Character@World" per running client (PID + start time, so a reused PID never inherits it).
// The optimizer keys main-client rules on this instead of the window title, which changes on every loading screen.
internal static class ClientIdentities
{
    private static readonly ConcurrentDictionary<int, (DateTime StartUtc, string Identity)> known = new();

    public static void Set(int processId, DateTime startUtc, string identity)
    {
        if (!string.IsNullOrWhiteSpace(identity)) known[processId] = (startUtc, identity);
    }

    public static string? Get(int processId, DateTime startUtc)
    {
        if (!known.TryGetValue(processId, out var entry)) return null;
        if (entry.StartUtc == startUtc) return entry.Identity;
        known.TryRemove(processId, out _);
        return null;
    }

    public static void Forget(int processId) => known.TryRemove(processId, out _);
}

internal sealed class ClientLabels : IDisposable
{
    private readonly ConcurrentDictionary<int, (DateTime Start, string Account)> owned = new();
    private readonly CancellationTokenSource stop = new();
    private Task? worker;

    public void Track(int processId, DateTime startUtc, string account)
    {
        owned[processId] = (startUtc, account);
        worker ??= Task.Run(MonitorAsync);
    }

    internal static string Title(string account, ExternalGameSnapshot state, string? lastKnownIdentity = null)
    {
        if (state.State == WorldReadiness.InWorld && GameWorldNames.All.TryGetValue(state.HomeWorld, out var world))
            return $"{state.CharacterName}@{world}";
        // A zone change does not change who is playing: keep the confirmed name through loading screens.
        if (state.State == WorldReadiness.Loading && !string.IsNullOrWhiteSpace(lastKnownIdentity))
            return lastKnownIdentity;
        // Account labels are never formatted as Character@World or treated as evidence of login.
        var label = new string(account.Where(c => !char.IsControl(c) && c != '@').Take(80).ToArray());
        return $"Potato Launcher — {label} — {(state.State == WorldReadiness.Loading ? "Loading" : "Client running")}";
    }

    private async Task MonitorAsync()
    {
        var reader = new ExternalGameState();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var (pid, entry) in owned)
                {
                    if (stop.IsCancellationRequested) return;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (process.HasExited || process.ProcessName != "ffxiv_dx11" || process.StartTime.ToUniversalTime() != entry.Start)
                        { owned.TryRemove(pid, out _); ClientIdentities.Forget(pid); continue; }
                        var state = reader.Read(pid, entry.Start);
                        // Leaving the world (title screen / character select) may mean a different character next.
                        if (state.State == WorldReadiness.NotInWorld) ClientIdentities.Forget(pid);
                        var title = Title(entry.Account, state, ClientIdentities.Get(pid, entry.Start));
                        if (state.State == WorldReadiness.InWorld && title.Contains('@')) ClientIdentities.Set(pid, entry.Start, title);
                        process.Refresh();
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero || process.MainWindowTitle == title) continue;
                        GetWindowThreadProcessId(window, out var owner);
                        if (owner != pid) continue;
                        // Bounded wait: a hung game must never freeze the launcher or its monitor.
                        SendMessageTimeout(window, 0x000C, IntPtr.Zero, title, 0x0002, 200, out _);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    { owned.TryRemove(pid, out _); ClientIdentities.Forget(pid); }
                }
                await Task.Delay(2000, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() { stop.Cancel(); }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}
