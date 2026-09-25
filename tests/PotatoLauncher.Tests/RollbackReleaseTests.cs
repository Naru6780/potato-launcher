using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace PotatoLauncher.Tests;

public class RollbackReleaseTests
{
    [Fact]
    public void SelectsThreeOlderStableVersionsNumerically()
    {
        var json = JsonSerializer.Serialize(new[] { 99, 104, 107, 106, 105, 108 }.Select(n => new {
            tag_name = $"v1.0.{n}", draft = false, prerelease = false, assets = new[] { new { name = "PotatoLauncher.zip" } }
        }));
        Assert.Equal(new[] { "v1.0.106", "v1.0.105", "v1.0.104" }, RollbackReleases.Parse(json, new(1, 0, 107, 0)).Select(r => r.Tag));
    }

    [Theory]
    [InlineData(true, false, "v1.0.106", "PotatoLauncher.zip")]
    [InlineData(false, true, "v1.0.106", "PotatoLauncher.zip")]
    [InlineData(false, false, "v1.0.106-preview.1", "PotatoLauncher.zip")]
    [InlineData(false, false, "v1.0.106", "settings.json")]
    public void RejectsUnavailableOrPreviewReleases(bool draft, bool prerelease, string tag, string asset)
    {
        var json = JsonSerializer.Serialize(new[] { new { tag_name = tag, draft, prerelease, assets = new[] { new { name = asset } } } });
        Assert.Empty(RollbackReleases.Parse(json, new(1, 0, 107, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractionNeverOverwritesProfileAndRejectsTraversal(bool traversal)
    {
        var root = Path.Combine(Path.GetTempPath(), "PotatoRollbackTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "release.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "Potato Launcher.exe", "Potato Launcher Assets/test.png", "settings.json" })
                { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("test"); }
                if (traversal) archive.CreateEntry("../escape.exe");
            }
            var destination = Path.Combine(root, "extract");
            if (traversal) Assert.Throws<InvalidDataException>(() => RollbackReleases.ExtractApplication(zip, destination));
            else
            {
                RollbackReleases.ExtractApplication(zip, destination);
                Assert.True(File.Exists(Path.Combine(destination, "Potato Launcher.exe")));
                Assert.False(File.Exists(Path.Combine(destination, "settings.json")));
            }
            Assert.False(File.Exists(Path.Combine(root, "escape.exe")));
        }
        finally { Directory.Delete(root, true); }
    }
}
