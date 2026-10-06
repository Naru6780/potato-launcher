using System.IO;

namespace PotatoLauncher;

internal static class AtomicTextFile
{
    // Write beside the destination so replacement stays on the same volume.
    // A failed serialization/write must not truncate the last usable configuration.
    internal static void Write(string path, string text)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
                writer.Flush();
                // Reach the disk before replacing, so a crash cannot leave an empty settings file.
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
