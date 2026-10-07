using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PotatoLauncher;

// Things outside Potato's control that cost clients frames, with what to do about them. Cheap checks, cached.
internal static class OptimizerDiagnostics
{
    private static (DateTime At, IReadOnlyList<string> Findings) cache = (DateTime.MinValue, []);

    public static IReadOnlyList<string> Get(IReadOnlyList<OptimizerClientSnapshot> clients, int targetFps)
    {
        var findings = new List<string>();
        var below = clients.Where(client => client.Fps is double fps && fps < targetFps - 3).ToList();
        if (below.Count > 0)
            findings.Add($"{below.Count} client{(below.Count == 1 ? " is" : "s are")} below {targetFps} FPS: {string.Join(", ", below.Select(client => client.ClientName).Take(4))}{(below.Count > 4 ? ", …" : "")}.");

        if (DateTime.UtcNow - cache.At > TimeSpan.FromSeconds(30)) cache = (DateTime.UtcNow, SlowChecks());
        findings.AddRange(cache.Findings);
        return findings;
    }

    private static IReadOnlyList<string> SlowChecks()
    {
        var findings = new List<string>();
        try
        {
            if (Process.GetProcessesByName("ProcessGovernor").Length > 0)
                findings.Add("Process Lasso: exclude ffxiv_dx11.exe from ProBalance so it stops demoting clients.");
        }
        catch { }
        var heavy = GloballyEnabledDalamudCollections();
        if (heavy.Plugins >= 15)
            findings.Add($"Dalamud collection{(heavy.Names.Count == 1 ? "" : "s")} {string.Join(", ", heavy.Names.Select(name => $"\"{name}\""))} load {heavy.Plugins} plugins in every client. Bind them to the characters that need them (Dalamud → Plugin Collections) to cut RAM and CPU per client.");
        return findings;
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
