using System.Diagnostics;

namespace PotatoLauncher;

internal sealed record RunningGameClient(int ProcessId, DateTime? StartTimeUtc, string Title);
internal sealed record BandClientLaunchResult(StartedGameClient? StartedClient, RunningGameClient? ExistingClient);

/// <summary>Read-only discovery. Never changes affinity, rendering, login settings, or existing clients.</summary>
internal sealed class RunningClientAwareness(Func<IReadOnlyList<RunningGameClient>> capture)
{
    private readonly Dictionary<string, RunningGameClient> tracked = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RunningGameClient> Capture()
    {
        var clients = new List<RunningGameClient>();
        foreach (var name in new[] { "ffxiv", "ffxiv_dx11" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        process.Refresh();
                        if (process.HasExited) continue;
                        // Start time is optional for title matching, but mandatory for PID tracking.
                        DateTime? startTime = null;
                        try { startTime = process.StartTime.ToUniversalTime(); }
                        catch (System.ComponentModel.Win32Exception) { }
                        clients.Add(new(process.Id, startTime, process.MainWindowTitle ?? ""));
                    }
                    catch (InvalidOperationException) { } // Client exited during discovery.
                    catch (System.ComponentModel.Win32Exception) { } // Inaccessible process.
                }
            }
        }
        return clients;
    }

    public void Track(string accountKey, int processId)
    {
        var client = capture().FirstOrDefault(item => item.ProcessId == processId);
        if (client?.StartTimeUtc is not null) tracked[accountKey] = client;
    }

    public async Task<BandClientLaunchResult> LaunchIfNeededAsync(
        Account account, string accountKey, string characterName, string world,
        Func<Account, CancellationToken, Task<StartedGameClient>> launch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var clients = capture(); // Fresh for every member, not cached at app startup or band selection.
        if (tracked.Remove(accountKey, out var previous))
        {
            var sameProcess = clients.FirstOrDefault(client =>
                client.ProcessId == previous.ProcessId && previous.StartTimeUtc is not null &&
                client.StartTimeUtc == previous.StartTimeUtc);
            if (sameProcess is not null)
            {
                tracked[accountKey] = sameProcess;
                return new(null, sameProcess);
            }
        }

        var namedClients = clients.Where(client => MatchesName(client.Title, characterName)).ToList();
        var match = namedClients.FirstOrDefault(client => string.IsNullOrWhiteSpace(world) ||
            client.Title[(client.Title.LastIndexOf('@') + 1)..].Trim().Equals(world.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            if (string.IsNullOrWhiteSpace(world) && namedClients.Count > 1)
                throw new InvalidOperationException($"Multiple running clients match {characterName}. Set this account's character/world before launching the band.");
            if (match.StartTimeUtc is not null) tracked[accountKey] = match;
            return new(null, match);
        }

        // A world visit or stale profile is not sufficient evidence to start a duplicate account.
        if (namedClients.Count > 0)
            throw new InvalidOperationException($"{characterName} is running with a different world in its window title. Verify the account's character/world before launching the band.");

        token.ThrowIfCancellationRequested();
        var started = await launch(account, token);
        Track(accountKey, started.ProcessId);
        return new(started, null);
    }

    internal static bool MatchesName(string title, string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return false;
        var separator = title.LastIndexOf('@');
        return separator > 0 && separator < title.TrimEnd().Length - 1 &&
            title[..separator].Trim().Equals(characterName.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
