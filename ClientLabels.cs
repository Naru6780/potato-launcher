using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotatoLauncher;

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

    internal static string Title(string account, ExternalGameSnapshot state)
    {
        if (state.State == WorldReadiness.InWorld && GameWorldNames.All.TryGetValue(state.HomeWorld, out var world))
            return $"{state.CharacterName}@{world}";
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
                        { owned.TryRemove(pid, out _); continue; }
                        var state = reader.Read(pid, entry.Start);
                        var title = Title(entry.Account, state);
                        process.Refresh();
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero || process.MainWindowTitle == title) continue;
                        GetWindowThreadProcessId(window, out var owner);
                        if (owner != pid) continue;
                        // Bounded wait: a hung game must never freeze the launcher or its monitor.
                        SendMessageTimeout(window, 0x000C, IntPtr.Zero, title, 0x0002, 200, out _);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    { owned.TryRemove(pid, out _); }
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
