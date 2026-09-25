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
            File.WriteAllText(temporaryPath, text);
            File.Move(temporaryPath, path, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
