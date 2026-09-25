using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace PotatoLauncher;

internal sealed record RollbackRelease(string Tag, Version Version)
{
    public string DownloadUrl => $"https://github.com/Naru6780/potato-launcher/releases/download/{Tag}/PotatoLauncher.zip";
    public override string ToString() => Tag;
}

internal static class RollbackReleases
{
    internal static IReadOnlyList<RollbackRelease> Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<RollbackRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var parsed) || parsed.Build < 0) continue;
            var version = new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
            // Canonical tags only: no arbitrary URL segments or preview labels.
            if (tag != $"v{version.Major}.{version.Minor}.{version.Build}" || version >= current) continue;
            if (!release.GetProperty("assets").EnumerateArray().Any(asset =>
                asset.GetProperty("name").GetString() == "PotatoLauncher.zip")) continue;
            releases.Add(new(tag, version));
        }
        return releases.DistinctBy(release => release.Version).OrderByDescending(release => release.Version).Take(3).ToArray();
    }

    internal static void ExtractApplication(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var selected = new List<(ZipArchiveEntry Entry, string Path)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            if (name.Contains(':') || name.Split('/').Any(part => part is ".." or "." or ""))
                throw new InvalidDataException("Unsafe path in release archive.");
            // Never copy a packaged profile, installer, updater script or unrelated application file.
            if (name != "Potato Launcher.exe" && !name.StartsWith("Potato Launcher Assets/", StringComparison.Ordinal)) continue;
            var path = Path.GetFullPath(Path.Combine(destination, name));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !seen.Add(path))
                throw new InvalidDataException("Invalid or duplicated release file.");
            bytes += entry.Length;
            if (bytes > 1024L * 1024 * 1024 || selected.Count >= 10000)
                throw new InvalidDataException("Release archive is unexpectedly large.");
            selected.Add((entry, path));
        }
        if (!selected.Any(item => item.Entry.FullName == "Potato Launcher.exe") ||
            !selected.Any(item => item.Path.StartsWith(Path.Combine(destination, "Potato Launcher Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Release is missing its executable or assets.");
        foreach (var item in selected)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
            item.Entry.ExtractToFile(item.Path, overwrite: false);
        }
    }
}
