using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PotatoLauncher;

// Optional per-account environment variables applied to the launcher process for that account only.
// Inherited by XIVLauncher -> Dalamud injector -> ffxiv_dx11.exe, so it can steer per-client injectors
// (e.g. RESHADE_BASE_PATH_OVERRIDE to give one client its own ReShade config and add-ons).
//
// File: %APPDATA%\Potato Launcher\launchEnvironment.json
// { "musicapotato13-False-False": { "RESHADE_BASE_PATH_OVERRIDE": "C:\\...\\game\\ReShade-Artemis" } }
internal static class AccountLaunchEnvironment
{
    internal static string FilePath() => Path.Combine(MainForm.PersistentDataRoot(), "launchEnvironment.json");

    internal static IReadOnlyDictionary<string, string> Load(string accountKey, string? path = null)
    {
        try
        {
            path ??= FilePath();
            if (string.IsNullOrWhiteSpace(accountKey) || !File.Exists(path)) return new Dictionary<string, string>();
            var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path));
            if (all is null) return new Dictionary<string, string>();
            var match = all.FirstOrDefault(entry => entry.Key.Equals(accountKey, StringComparison.OrdinalIgnoreCase));
            return match.Value is null
                ? new Dictionary<string, string>()
                : match.Value
                    .Where(variable => !string.IsNullOrWhiteSpace(variable.Key))
                    .ToDictionary(variable => variable.Key.Trim(), variable => Environment.ExpandEnvironmentVariables(variable.Value ?? ""));
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    internal static int Apply(ProcessStartInfo startInfo, string accountKey, string? path = null)
    {
        var variables = Load(accountKey, path);
        foreach (var (name, value) in variables)
        {
            startInfo.Environment[name] = value;
        }
        return variables.Count;
    }
}
