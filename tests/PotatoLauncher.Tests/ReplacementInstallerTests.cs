using System.Diagnostics;

namespace PotatoLauncher.Tests;

public class ReplacementInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"replace-{Guid.NewGuid():N}");
    private string Target => Path.Combine(root, "app");
    private string Extract => Path.Combine(root, "temp", "extract");
    private string Temp => Path.Combine(root, "temp");
    private string Profile => Path.Combine(root, "profile");
    private string Backup => Path.Combine(root, "backup");

    public ReplacementInstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(Target, "Potato Launcher Assets"));
        File.WriteAllText(Path.Combine(Target, "Potato Launcher.exe"), "old exe");
        File.WriteAllText(Path.Combine(Target, "Potato Launcher Assets", "a.txt"), "old asset");
        Directory.CreateDirectory(Path.Combine(Extract, "Potato Launcher Assets"));
        File.WriteAllText(Path.Combine(Extract, "Potato Launcher Assets", "a.txt"), "new asset");
        Directory.CreateDirectory(Profile);
        File.WriteAllText(Path.Combine(Profile, "settings.json"), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void ReplacesExeAndAssetsAfterBackingUp()
    {
        File.WriteAllText(Path.Combine(Extract, "Potato Launcher.exe"), "new exe");

        Run();

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(Target, "Potato Launcher.exe")));
        Assert.Equal("new asset", File.ReadAllText(Path.Combine(Target, "Potato Launcher Assets", "a.txt")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(Backup, "Potato Launcher.exe")));
        Assert.Equal("old asset", File.ReadAllText(Path.Combine(Backup, "Potato Launcher Assets", "a.txt")));
        Assert.True(File.Exists(Path.Combine(Backup, "profile", "settings.json")));
        Assert.False(Directory.Exists(Temp));
    }

    [Fact]
    public void UpdatesAPortableInstallThatHasNoAssetsFolder()
    {
        Directory.Delete(Path.Combine(Target, "Potato Launcher Assets"), recursive: true);
        File.WriteAllText(Path.Combine(Extract, "Potato Launcher.exe"), "new exe");

        Run();

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(Target, "Potato Launcher.exe")));
        Assert.Equal("new asset", File.ReadAllText(Path.Combine(Target, "Potato Launcher Assets", "a.txt")));
        Assert.False(File.Exists(Path.Combine(Backup, "failure.txt")));
    }

    [Fact]
    public void RestoresTheBackupWhenACopyFailsHalfway()
    {
        // No new exe in the package: assets are copied first, then the exe copy fails.
        Run();

        Assert.Equal("old exe", File.ReadAllText(Path.Combine(Target, "Potato Launcher.exe")));
        Assert.Equal("old asset", File.ReadAllText(Path.Combine(Target, "Potato Launcher Assets", "a.txt")));
        Assert.True(File.Exists(Path.Combine(Backup, "failure.txt")));
        Assert.False(Directory.Exists(Temp));
    }

    private void Run()
    {
        var script = MainForm.BuildReplacementScript(Extract, Target, Path.Combine(Target, "Potato Launcher.exe"), Backup,
            Profile, Temp, waitProcessId: 0, "Update failed", "Potato Launcher update", restart: false);
        var scriptPath = Path.Combine(root, "update.ps1");
        File.WriteAllText(scriptPath, script);
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        })!;
        var errors = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "installer script timed out");
        Assert.True(string.IsNullOrWhiteSpace(errors), errors);
    }
}

public class ReplacementInstallerSyntaxTests
{
    [Fact]
    public void RealInstallerScriptParsesWithoutErrors()
    {
        var script = MainForm.BuildReplacementScript(@"C:\x it's\extract", @"C:\app", @"C:\app\Potato Launcher.exe", @"C:\b",
            @"C:\p", @"C:\t", 1234, "Update failed", "Potato Launcher update", restart: true);
        var path = Path.Combine(Path.GetTempPath(), $"parse-{Guid.NewGuid():N}.ps1");
        var checker = Path.Combine(Path.GetTempPath(), $"parse-check-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(path, script);
        File.WriteAllText(checker, $"$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$e); Write-Output $e.Count");
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{checker}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true })!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd().Trim();
            Assert.True(process.WaitForExit(60_000), "parser timed out");
            Assert.Equal("0", output);
            Assert.Contains("MessageBox]::Show(\"Update failed: $failure", script);
            Assert.Contains("Start-Process -FilePath $exe", script);
            Assert.Contains(@"'C:\x it''s\extract'", script);
        }
        finally { File.Delete(path); File.Delete(checker); }
    }
}
