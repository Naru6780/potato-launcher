using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PotatoLauncher;

// Things outside Potato's control that cost clients frames, with what to do about them. Cheap checks, cached.
internal static class OptimizerDiagnostics
{
    private static (DateTime At, IReadOnlyList<string> Findings, IReadOnlySet<int> LoaderClients) cache = (DateTime.MinValue, [], new HashSet<int>());

    public static IReadOnlyList<string> Get(IReadOnlyList<OptimizerClientSnapshot> clients, int targetFps, (int Priority, int Affinity) externalChanges = default)
    {
        if (DateTime.UtcNow - cache.At > TimeSpan.FromSeconds(30)) cache = SlowChecks(clients.Select(client => client.ProcessId));
        return Evaluate(clients, targetFps, externalChanges, cache.LoaderClients, cache.Findings);
    }

    internal static IReadOnlyList<string> Evaluate(IReadOnlyList<OptimizerClientSnapshot> clients, int targetFps,
        (int Priority, int Affinity) externalChanges, IReadOnlySet<int> loaderClients, IReadOnlyList<string> slowFindings)
    {
        var findings = new List<string>();
        // Measured: the DLSS 5 loader (ReShade + RenoDX) adds ~3.7 ms per frame on top of the game's own limiter.
        var loaded = clients.Where(client => loaderClients.Contains(client.ProcessId) && client.EngineFrameLimit > 0 && client.Fps is double fps && fps < targetFps - 3).ToList();
        foreach (var client in loaded.Take(2))
            findings.Add($"{client.ClientName} loads the DLSS 5 add-on and has the in-game {client.EngineFrameLimit} fps limit, so it holds only ~{client.Fps:0}. Not using DLSS: untick it in DLSS 5 clients and relaunch it. Using DLSS: set its Frame Rate to None.");
        if (externalChanges.Priority >= 3)
            findings.Add($"Another program changed client priorities {externalChanges.Priority} times in the last minute (Process Lasso's ProBalance does this to the busiest client, usually your main). Close Process Lasso, or exclude ffxiv_dx11.exe from ProBalance.");
        if (externalChanges.Affinity >= 2)
            findings.Add($"Another program keeps changing client CPU cores ({externalChanges.Affinity} times in the last minute), which undoes Potato's placement. Remove ffxiv_dx11.exe CPU affinity / CPU set rules in Process Lasso or similar tools.");

        var below = clients.Where(client => client.Fps is double fps && fps < targetFps - 3).ToList();
        if (below.Count > 0)
            findings.Add($"{below.Count} client{(below.Count == 1 ? " is" : "s are")} below {targetFps} FPS: {string.Join(", ", below.Select(client => client.ClientName).Take(4))}{(below.Count > 4 ? ", …" : "")}.");

        // A client with no in-game limit is fine while the NVIDIA driver cap paces it (DLSS 5 clients run that way on
        // purpose); it is a problem once it actually runs above the target, which happens when covered or minimized.
        var uncapped = clients.Where(client => client.EngineFrameLimit == 0 && client.Fps is double fps && fps > targetFps + 5).ToList();
        if (uncapped.Count > 0)
            findings.Add($"{uncapped.Count} client{(uncapped.Count == 1 ? " is" : "s are")} running above the target with no in-game frame limit ({string.Join(", ", uncapped.Select(client => $"{client.ClientName} {client.Fps:0} FPS").Take(4))}{(uncapped.Count > 4 ? ", …" : "")}). " +
                         $"The driver cap only holds while the window is on screen. Fix: System Configuration → Display Settings → Frame Rate → {targetFps} fps in that client (not for a DLSS 5 client: keep it on screen instead).");
        var held = clients.Where(client => client.HeldByPotato && client.EngineFrameLimit == 0).ToList();
        if (held.Count > 0)
            findings.Add($"{held.Count} client{(held.Count == 1 ? " has" : "s have")} no in-game frame limit and would run away, so Potato is holding {(held.Count == 1 ? "it" : "them")} at {targetFps} with a CPU cap ({string.Join(", ", held.Select(client => client.ClientName).Take(3))}{(held.Count > 3 ? ", …" : "")}). " +
                         $"The game's own limit is cheaper and steadier: System Configuration → Display Settings → Frame Rate → {targetFps} fps in {(held.Count == 1 ? "it" : "them")}.");
        var above = clients.Where(client => client.EngineFrameLimit != 0 && client.Fps is double fps && fps > targetFps * 1.5).ToList();
        if (above.Count > 0)
            findings.Add($"{above.Count} client{(above.Count == 1 ? " is" : "s are")} running far above the target ({above.Max(client => client.Fps)!.Value:0} FPS) despite an in-game limit; check its Frame Rate setting.");

        findings.AddRange(slowFindings);
        return findings;
    }

    private static (DateTime, IReadOnlyList<string>, IReadOnlySet<int>) SlowChecks(IEnumerable<int> clientIds)
    {
        var findings = new List<string>();
        try
        {
            if (Process.GetProcessesByName("ProcessGovernor").Length > 0)
                findings.Add("Process Lasso is running. Potato already does its job for FFXIV, and its ProBalance and CPU rules fight Potato's priorities and core placement. Close it, or exclude ffxiv_dx11.exe from ProBalance and remove its ffxiv_dx11.exe rules.");
        }
        catch { }
        var loaderClients = new HashSet<int>();
        foreach (var id in clientIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Contains("renodx", StringComparison.OrdinalIgnoreCase)))
                    loaderClients.Add(id);
            }
            catch { }
        }
        var heavy = GloballyEnabledDalamudCollections();
        if (heavy.Plugins >= 15)
            findings.Add($"Dalamud collection{(heavy.Names.Count == 1 ? "" : "s")} {string.Join(", ", heavy.Names.Select(name => $"\"{name}\""))} load {heavy.Plugins} plugins in every client. Bind them to the characters that need them (Dalamud → Plugin Collections) to cut RAM and CPU per client.");
        return (DateTime.UtcNow, findings, loaderClients);
    }

    // Collections that are enabled and not tied to specific characters are loaded by every client.
    internal static (IReadOnlyList<string> Names, int Plugins) GloballyEnabledDalamudCollections(string? configPath = null)
    {
        try
        {
            configPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "dalamudConfig.json");
            if (!File.Exists(configPath)) return ([], 0);
            using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("SavedProfiles", out var saved) || !saved.TryGetProperty("$values", out var profiles)) return ([], 0);
            var names = new List<string>();
            var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles.EnumerateArray())
            {
                var enabled = profile.TryGetProperty("e", out var e) && e.ValueKind == JsonValueKind.True;
                var characters = profile.TryGetProperty("pc", out var pc) && pc.TryGetProperty("$values", out var list) ? list.GetArrayLength() : 0;
                if (!enabled || characters > 0) continue;
                names.Add(profile.TryGetProperty("n", out var n) ? n.GetString() ?? "?" : "?");
                if (profile.TryGetProperty("Plugins", out var p) && p.TryGetProperty("$values", out var entries))
                    foreach (var entry in entries.EnumerateArray())
                        if (entry.TryGetProperty("IsEnabled", out var on) && on.ValueKind == JsonValueKind.True && entry.TryGetProperty("InternalName", out var name))
                            plugins.Add(name.GetString() ?? "");
            }
            return (names, plugins.Count);
        }
        catch
        {
            return ([], 0);
        }
    }
}
