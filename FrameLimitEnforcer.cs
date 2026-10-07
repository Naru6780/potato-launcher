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
    public static string Apply(int targetFps, string? configPath = null)
    {
        try
        {
            configPath ??= DefaultConfigPath();
            if (!File.Exists(configPath)) return "";
            var text = File.ReadAllText(configPath);
            var desired = OptionForTarget(targetFps);
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
