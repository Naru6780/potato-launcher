using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PotatoLauncher;

// Lets the user pick exactly which accounts start with DLSS 5 (ReShade + RenoDX add-on in the game folder).
//
// All clients share one game folder, so ReShade and its add-ons normally load everywhere. Potato splits it:
// - the game folder's ReShade.ini searches add-ons in an empty folder, so other clients run ReShade without add-ons;
// - DLSS clients start with RESHADE_BASE_PATH_OVERRIDE pointing at Potato's own ReShade.ini, which loads add-ons
//   from the game folder.
// The split is re-applied before every launch, so a DLSS installer rewriting the game's ReShade.ini cannot turn
// DLSS back on for every client.
internal sealed class Dlss5Config
{
    public List<string> AccountKeys { get; set; } = [];
    public string GameFolderOverride { get; set; } = "";
    // When at least one DLSS 5 client is chosen: start every other client with ReShade switched off entirely
    // (no banner, overlay or effects) instead of ReShade without add-ons.
    public bool ReShadeOffForOtherClients { get; set; } = true;
}

internal sealed record Dlss5Status(string GameFolder, bool ReShadeFound, IReadOnlyList<string> AddOns)
{
    public bool Available => ReShadeFound && AddOns.Count > 0;

    public string Describe()
    {
        if (string.IsNullOrWhiteSpace(GameFolder)) return "FFXIV game folder not found.";
        if (!ReShadeFound) return "ReShade was not found in the game folder (expected ReShade.ini).";
        if (AddOns.Count == 0) return "No ReShade add-on (such as renodx-dlss.addon64) found in the game folder.";
        return $"Found: {string.Join(", ", AddOns)}";
    }
}

internal static class Dlss5Clients
{
    internal const string BasePathVariable = "RESHADE_BASE_PATH_OVERRIDE";

    internal static string ConfigPath(string? dataRoot = null) => Path.Combine(dataRoot ?? MainForm.PersistentDataRoot(), "dlss5.json");
    internal static string DlssProfileFolder(string? dataRoot = null) => Path.Combine(dataRoot ?? MainForm.PersistentDataRoot(), "DLSS5", "ReShade");
    internal static string NoAddOnsFolder(string? dataRoot = null) => Path.Combine(dataRoot ?? MainForm.PersistentDataRoot(), "DLSS5", "NoAddOns");

    internal static Dlss5Config Load(string? dataRoot = null)
    {
        try
        {
            var path = ConfigPath(dataRoot);
            if (File.Exists(path)) return JsonSerializer.Deserialize<Dlss5Config>(File.ReadAllText(path)) ?? new Dlss5Config();
        }
        catch { }
        return new Dlss5Config();
    }

    internal static void Save(Dlss5Config config, string? dataRoot = null)
    {
        var path = ConfigPath(dataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        config.AccountKeys = config.AccountKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        AtomicTextFile.Write(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static bool IsEnabled(Dlss5Config config, string accountKey) =>
        config.AccountKeys.Contains(accountKey, StringComparer.OrdinalIgnoreCase);

    internal static string FindGameFolder(Dlss5Config config, string sharedProfileFolder)
    {
        if (IsGameFolder(config.GameFolderOverride)) return Path.GetFullPath(config.GameFolderOverride);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var profile in new[] { sharedProfileFolder, Path.Combine(appData, "XIVLauncher") })
        {
            var gamePath = ReadXivLauncherGamePath(profile);
            if (!string.IsNullOrWhiteSpace(gamePath))
            {
                var game = Path.Combine(gamePath, "game");
                if (IsGameFolder(game)) return game;
            }
        }
        var defaultGame = @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game";
        return IsGameFolder(defaultGame) ? defaultGame : "";
    }

    internal static bool IsGameFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "ffxiv_dx11.exe"));

    private static string ReadXivLauncherGamePath(string profileFolder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileFolder)) return "";
            var path = Path.Combine(Environment.ExpandEnvironmentVariables(profileFolder), "launcherConfigV3.json");
            if (!File.Exists(path)) return "";
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("GamePath", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    internal static Dlss5Status Inspect(string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return new Dlss5Status("", false, []);
        var reshade = File.Exists(Path.Combine(gameFolder, "ReShade.ini"));
        var addOns = Directory.EnumerateFiles(gameFolder, "*.addon64")
            .Concat(Directory.EnumerateFiles(gameFolder, "*.addon"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Dlss5Status(gameFolder, reshade, addOns);
    }

    // Re-applies the split. Returns an error message, or "" on success.
    internal static string EnsureSplit(string gameFolder, string? dataRoot = null)
    {
        try
        {
            var status = Inspect(gameFolder);
            if (!status.Available) return status.Describe();

            var sharedIniPath = Path.Combine(gameFolder, "ReShade.ini");
            var sharedIni = File.ReadAllText(sharedIniPath);
            var noAddOns = NoAddOnsFolder(dataRoot);
            Directory.CreateDirectory(noAddOns);

            var dlssFolder = DlssProfileFolder(dataRoot);
            Directory.CreateDirectory(dlssFolder);
            var dlssIniPath = Path.Combine(dlssFolder, "ReShade.ini");
            var dlssIni = File.Exists(dlssIniPath)
                ? File.ReadAllText(dlssIniPath)
                : AbsolutizeRelativePaths(sharedIni, gameFolder);
            WriteIfChanged(dlssIniPath, dlssIni, SetIniValue(dlssIni, "ADDON", "AddonPath", gameFolder));

            WriteIfChanged(sharedIniPath, sharedIni, SetIniValue(sharedIni, "ADDON", "AddonPath", noAddOns));
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "Windows denied writing ReShade.ini in the game folder. Run Potato once as administrator, or allow your user to modify that file.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Applies the DLSS base path to this account's launch only. Returns true when DLSS 5 is active for it.
    internal static bool ApplyToLaunch(ProcessStartInfo startInfo, string accountKey, string sharedProfileFolder, out string message)
    {
        var config = Load();
        return ApplyToLaunch(startInfo, accountKey, config, FindGameFolder(config, sharedProfileFolder), null, out message);
    }

    internal static bool ApplyToLaunch(ProcessStartInfo startInfo, string accountKey, Dlss5Config config, string gameFolder, string? dataRoot, out string message)
    {
        message = "";
        const string reShadeOffFailure = "Could not prepare the ReShade-off profile; this client starts with ReShade (no add-ons).";
        if (config.AccountKeys.Count == 0)
        {
            // No DLSS 5 client at all: "ReShade off" still applies to every client, otherwise the dxgi proxy in the
            // game folder loads full ReShade (banner, overlay) for all of them.
            if (config.ReShadeOffForOtherClients && Inspect(gameFolder).ReShadeFound && !TryApplyReShadeOff(startInfo, dataRoot))
                message = reShadeOffFailure;
            return false;
        }

        var error = EnsureSplit(gameFolder, dataRoot);
        if (!string.IsNullOrEmpty(error))
        {
            message = $"DLSS 5 not applied: {error}";
            return false;
        }
        if (!IsEnabled(config, accountKey))
        {
            if (config.ReShadeOffForOtherClients && !TryApplyReShadeOff(startInfo, dataRoot)) message = reShadeOffFailure;
            return false;
        }
        startInfo.Environment[BasePathVariable] = DlssProfileFolder(dataRoot);
        return true;
    }

    internal static string ReShadeOffFolder(string? dataRoot = null) => Path.Combine(dataRoot ?? MainForm.PersistentDataRoot(), "DLSS5", "ReShadeOff");

    // ReShade has no "off" switch. Verified with ReShade 6.8 as a dxgi.dll proxy: disabling graphics hooks alone
    // crashes the game, because the proxy's exports are only wired to the real dxgi.dll inside the graphics-hook setup.
    // With its own base path whose ReShade.ini forwards the exports to the system dxgi.dll, the process runs normally
    // and ReShade never creates a runtime (no banner, overlay, effects or add-ons).
    internal static bool TryApplyReShadeOff(ProcessStartInfo startInfo, string? dataRoot = null)
    {
        var folder = EnsureReShadeOffProfile(dataRoot);
        if (folder is null) return false; // Never set the disable switches without the forwarding profile.
        startInfo.Environment["RESHADE_DISABLE_GRAPHICS_HOOK"] = "1";
        startInfo.Environment["RESHADE_DISABLE_INPUT_HOOK"] = "1";
        startInfo.Environment[BasePathVariable] = folder;
        return true;
    }

    internal static string? EnsureReShadeOffProfile(string? dataRoot = null)
    {
        try
        {
            var systemDxgi = Path.Combine(Environment.SystemDirectory, "dxgi.dll");
            if (!File.Exists(systemDxgi)) return null;
            var folder = ReShadeOffFolder(dataRoot);
            Directory.CreateDirectory(folder);
            var iniPath = Path.Combine(folder, "ReShade.ini");
            var ini = $"[PROXY]\r\nEnableProxyLibrary=1\r\nProxyLibrary={systemDxgi}\r\n\r\n[INSTALL]\r\nLogging=0\r\n";
            if (!File.Exists(iniPath) || File.ReadAllText(iniPath) != ini) File.WriteAllText(iniPath, ini, new UTF8Encoding(false));
            return folder;
        }
        catch
        {
            return null;
        }
    }

    internal static string SetIniValue(string ini, string section, string key, string value)
    {
        var newline = ini.Contains("\r\n") ? "\r\n" : "\n";
        var lines = ini.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var sectionHeader = $"[{section}]";
        var start = lines.FindIndex(line => line.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.AddRange(["", sectionHeader, $"{key}={value}", ""]);
            return string.Join(newline, lines);
        }

        var end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (var index = start + 1; index < end; index++)
        {
            var separator = lines[index].IndexOf('=');
            if (separator > 0 && lines[index][..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = $"{key}={value}";
                return string.Join(newline, lines);
            }
        }
        lines.Insert(start + 1, $"{key}={value}");
        return string.Join(newline, lines);
    }

    // ReShade resolves ".\" against its base path; the DLSS profile lives elsewhere, so point those at the game folder.
    internal static string AbsolutizeRelativePaths(string ini, string gameFolder)
    {
        var folder = gameFolder.TrimEnd('\\', '/') + "\\";
        return Regex.Replace(ini, @"(?<=[=,])\.\\", _ => folder);
    }

    private static void WriteIfChanged(string path, string original, string updated)
    {
        if (File.Exists(path) && string.Equals(original, updated, StringComparison.Ordinal)) return;
        File.WriteAllText(path, updated, new UTF8Encoding(false));
    }
}
