using System.IO;
using PotatoLauncher;
using Xunit;

public sealed class PreserveUnreadableFileTests
{
    [Fact]
    public void KeepsACopyOfAnUnreadableFileBeforeDefaultsAreWritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"preserve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{ not json");

            Assert.True(MainForm.PreserveUnreadableFile(path));
            var copy = Assert.Single(Directory.GetFiles(folder, "settings.json.corrupt-*"));
            Assert.Equal("{ not json", File.ReadAllText(copy));
            Assert.True(MainForm.PreserveUnreadableFile(Path.Combine(folder, "missing.json")));
            Assert.True(MainForm.PreserveUnreadableFile(""));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
