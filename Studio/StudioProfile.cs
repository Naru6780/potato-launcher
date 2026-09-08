using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PotatoLauncher.Studio;

internal sealed class StudioButton
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "New button";
    public string Command { get; set; } = "";
    public List<StudioStep> Steps { get; set; } = [];
    public bool IsGroup { get; set; }
    public int IconId { get; set; }
    public string Artwork { get; set; } = "";
    public string Background { get; set; } = "#29243E";
    public string Foreground { get; set; } = "#FFFFFF";
    public string ImportWarning { get; set; } = "";
    public List<StudioButton> Children { get; set; } = [];
    public override string ToString() => Label;
}

internal sealed class StudioProfile
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Command Studio";
    public string ImportedFrom { get; set; } = "";
    public int TileSize { get; set; } = 96;
    public bool Animate { get; set; } = true;
    public List<StudioButton> Buttons { get; set; } = [];
}

internal static class StudioProfiles
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 64 };
    public static string DefaultQoLBarPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "QoLBar.json");

    public static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("Configuration is too large (maximum 8 MB).");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (output.Length + count > MaxFileBytes) throw new InvalidDataException("Configuration is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public static StudioProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<StudioProfile>(ReadBounded(path), Json) ?? throw new InvalidDataException("Empty studio profile.");
        Validate(profile);
        return profile;
    }

    public static void Save(string path, StudioProfile profile)
    {
        Validate(profile);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, Json);
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Studio profile is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Validate(StudioProfile profile)
    {
        if (profile.Version != 1) throw new InvalidDataException("Unsupported studio profile version; no changes were saved.");
        if (profile.Name is null || profile.Name.Length > 200 || profile.ImportedFrom is null || profile.Buttons is null || profile.TileSize is < 64 or > 160)
            throw new InvalidDataException("Invalid studio settings.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Visit(List<StudioButton> buttons, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Groups may be nested at most 16 levels.");
            foreach (var button in buttons)
            {
                if (button is null || !Guid.TryParseExact(button.Id, "N", out _) || !ids.Add(button.Id) || ids.Count > 2000 ||
                    button.Label is null || button.Label.Length > 200 || button.Command is null || button.Command.Length > 32000 ||
                    button.Artwork is null || button.Artwork.Length > 4096 || button.ImportWarning is null || button.ImportWarning.Length > 2000 ||
                    button.Children is null || button.Steps is null || button.Steps.Count > 64 ||
                    button.Steps.Any(step => step is null || step.Command is null || step.Command.Length > 32000 || step.DelayAfterMs is < 0 or > 600000) ||
                    !IsColor(button.Background) || !IsColor(button.Foreground))
                    throw new InvalidDataException("Invalid button data, duplicate ID, or too many buttons (maximum 2000).");
                Visit(button.Children, depth + 1);
            }
        }
        Visit(profile.Buttons, 0);
    }

    public static bool IsColor(string? color) => color is not null && Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$");

    public static IReadOnlyList<StudioStep> StepsFor(StudioButton button) => button.Steps.Count > 0 ? button.Steps :
        button.Command.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => new StudioStep(line, 180)).ToArray();

    public static StudioButton Duplicate(StudioButton source)
    {
        var copy = JsonSerializer.Deserialize<StudioButton>(JsonSerializer.Serialize(source, Json), Json)!;
        void Renew(StudioButton button) { button.Id = Guid.NewGuid().ToString("N"); foreach (var child in button.Children) Renew(child); }
        Renew(copy);
        return copy;
    }

    public static IReadOnlyList<string> BarNames(string path)
    {
        using var json = JsonDocument.Parse(ReadBounded(path), new JsonDocumentOptions { MaxDepth = 64 });
        return json.RootElement.GetProperty("BarCfgs").EnumerateArray().Select(bar => Text(bar, "n")).ToArray();
    }

    public static StudioProfile Import(string path, string barName)
    {
        using var json = JsonDocument.Parse(ReadBounded(path), new JsonDocumentOptions { MaxDepth = 64 });
        var matches = json.RootElement.GetProperty("BarCfgs").EnumerateArray().Where(bar => Text(bar, "n") == barName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Choose a uniquely named QoLBar bar.");
        var profile = new StudioProfile { Name = barName, ImportedFrom = Path.GetFullPath(path) };
        var count = 0;
        List<StudioButton> ReadChildren(JsonElement parent, int depth)
        {
            if (depth > 16) throw new InvalidDataException("QoLBar group nesting exceeds 16 levels.");
            var result = new List<StudioButton>();
            if (!parent.TryGetProperty("sL", out var children) || children.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in children.EnumerateArray())
            {
                if (++count > 2000) throw new InvalidDataException("Bar exceeds 2000 buttons.");
                var rawName = Text(item, "n");
                var match = Regex.Match(rawName, @"::[A-Za-z]*(-?\d+)");
                var iconId = match.Success && int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : 0;
                var separator = rawName.IndexOf("##", StringComparison.Ordinal);
                var label = separator >= 0 && separator + 2 < rawName.Length ? rawName[(separator + 2)..] : Regex.Replace(rawName, @"::[A-Za-z]*-?\d+(##)?", "");
                var type = Number(item, "t");
                var mode = Number(item, "m");
                var button = new StudioButton
                {
                    Label = string.IsNullOrWhiteSpace(label) ? $"Icon {iconId}" : label,
                    Command = Text(item, "c"),
                    IconId = iconId,
                    Artwork = iconId < 0 ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "QoLBar", "icons", $"{-(long)iconId}.png") : "",
                    ImportWarning = mode != 0 ? "QoLBar incremental/random mode is not supported. Edit this into an explicit command before running." :
                        type is not (0 or 1) ? "This QoLBar shortcut type is not supported. Edit it into an explicit command before running." : "",
                    Children = ReadChildren(item, depth + 1),
                    IsGroup = type == 1
                };
                if (button.IsGroup && !string.IsNullOrWhiteSpace(button.Command))
                {
                    button.Children.Insert(0, new StudioButton
                    {
                        Label = "Use " + button.Label,
                        Command = button.Command,
                        IconId = button.IconId,
                        Artwork = button.Artwork,
                        ImportWarning = button.ImportWarning
                    });
                    button.Command = "";
                }
                result.Add(button);
            }
            return result;
        }
        profile.Buttons = ReadChildren(matches[0], 0);
        Validate(profile);
        return profile;
    }

    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Number(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
