using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PotatoLauncher;

// Keeps the game's own frame limiter on for every client Potato launches.
//
// Why: the NVIDIA driver cap only paces frames that are actually presented. A covered window runs ~110 loops/s
// and a minimized one 250-400, doubling or tripling its CPU. The game's limiter (System Configuration > Display >
// Frame Rate) sleeps inside the game loop, so it holds on top, covered or minimized (measured: 58-60/s covered,
// ~2.8% CPU per client on a 9800X3D).
//
// The setting lives in the shared FFXIV.cfg ("Fps" line). Every client rewrites that file when it saves settings
// or exits, so Potato re-applies the value right before each launch; a running client only picks it up when
// relaunched. Values on the current build (config table min 0, max 3): 0 none, 1 main display refresh rate,
// 2 = 60 fps, 3 = 30 fps.
internal static class FrameLimitEnforcer
{
    internal const int FpsNone = 0;
    internal const int FpsRefreshRate = 1;
    internal const int Fps60 = 2;
    internal const int Fps30 = 3;

    public static string DefaultConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "FINAL FANTASY XIV - A Realm Reborn", "FFXIV.cfg");

    // Smallest built-in cap that still reaches the target; the display refresh rate only when the target is above 60.
    internal static int OptionForTarget(int targetFps) => targetFps <= 30 ? Fps30 : targetFps <= 60 ? Fps60 : FpsRefreshRate;

    internal static string Describe(int option) => option switch
    {
        FpsNone => "none",
        FpsRefreshRate => "display refresh rate",
        Fps60 => "60 fps",
        Fps30 => "30 fps",
        _ => $"option {option}"
    };

    // Returns a message for the status bar when the file was changed, "" when it already matched or does not exist.
    // Throws nothing: a failure to write only means this launch keeps the previous value.
    public static string Apply(int targetFps, string? configPath = null) => ApplyOption(OptionForTarget(targetFps), configPath);

    /// <summary>
    /// Frame Rate option to write before launching one client. Followers always get the game's own limit: render-cut,
    /// covered and minimized windows present nothing for the NVIDIA cap to pace. The configured main may get None so
    /// the NVIDIA cap holds it at an exact target (the game's limit lands at ~56-58; measured on a 9800X3D the NVIDIA
    /// cap costs ~0.8 point of CPU more for that one client), but only when that cap is actually set to the target:
    /// otherwise the main would run uncapped (seen: 121 FPS).
    /// </summary>
    internal static int LaunchOption(bool dlss5Client, bool isMain, bool mainUsesDriverCap, int? driverCapFps, int targetFps)
    {
        if (dlss5Client) return FpsNone;
        if (isMain && mainUsesDriverCap && driverCapFps is int cap && cap == targetFps) return FpsNone;
        return OptionForTarget(targetFps);
    }

    // DLSS 5 clients get FpsNone: their loader (RenoDX) adds a fixed per-frame cost that serializes with the game's
    // limiter (60 fps limit + loader = ~48), while the NVIDIA driver cap paces them at 60 with that cost hidden inside
    // the frame. Measured 2026-10-07.
    public static string ApplyOption(int desired, string? configPath = null)
    {
        try
        {
            configPath ??= DefaultConfigPath();
            if (!File.Exists(configPath)) return "";
            var text = File.ReadAllText(configPath);
            var updated = SetOption(text, "Fps", desired, out var previous);
            if (previous == desired) return "";
            if (previous < 0) return "";
            File.WriteAllText(configPath, updated, new UTF8Encoding(false));
            return $"In-game frame limit set to {Describe(desired)} for new clients (was {Describe(previous)}).";
        }
        catch
        {
            return "";
        }
    }

    public static int? CurrentOption(string? configPath = null)
    {
        try
        {
            configPath ??= DefaultConfigPath();
            if (!File.Exists(configPath)) return null;
            SetOption(File.ReadAllText(configPath), "Fps", 0, out var current);
            return current < 0 ? null : current;
        }
        catch
        {
            return null;
        }
    }

    // Replaces the value of "<key><tab><value>" in place, keeping every other byte of the file (line endings
    // included). previous is -1 when the key is missing.
    internal static string SetOption(string text, string key, int value, out int previous)
    {
        previous = -1;
        var match = Regex.Match(text, $@"(?m)^(\s*{Regex.Escape(key)}\t)(\d+)(?=\r?$)");
        if (!match.Success) return text;
        previous = int.Parse(match.Groups[2].Value);
        return text[..match.Groups[2].Index] + value + text[(match.Groups[2].Index + match.Groups[2].Length)..];
    }
}
