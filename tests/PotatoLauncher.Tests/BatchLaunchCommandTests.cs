namespace PotatoLauncher.Tests;

public class BatchLaunchCommandTests : IDisposable
{
    private readonly List<string> temporaryDirectories = [];

    [Fact]
    public void BuildBatchLaunchCommand_FindsStartAfterBatchPreamble()
    {
        var batchPath = CreateBatchFile(
            "@echo off",
            ":: generated launcher command",
            "start \"\" \"C:\\Tools\\XIVLauncher.exe\" --account=old-False-False --Autologinenabled=true");
        var account = new Account("Potato", Path.GetFileName(batchPath), 0, "potato-False-False");

        var command = MainForm.BuildBatchLaunchCommand(batchPath, account, Path.GetDirectoryName(batchPath)!);

        Assert.Equal("C:\\Tools\\XIVLauncher.exe", command.FileName);
        Assert.Equal("--account=potato-False-False --Autologinenabled=true", command.Arguments);
    }

    [Fact]
    public void BuildBatchLaunchCommand_ExpandsQuotedLocalVariables()
    {
        var batchPath = CreateBatchFile(
            "set \"LAUNCHER_ROOT=C:\\Program Files\\XIVLauncher\"",
            "@set \"LAUNCHER_EXE=%LAUNCHER_ROOT%\\XIVLauncher.exe\"",
            "@start /d \"%LAUNCHER_ROOT%\" \"\" \"%LAUNCHER_EXE%\" --account old-False-False");
        var account = new Account("OTP Potato", Path.GetFileName(batchPath), 0, "potato-True-False", UseOtp: true);

        var command = MainForm.BuildBatchLaunchCommand(batchPath, account, Path.GetDirectoryName(batchPath)!);

        Assert.Equal("C:\\Program Files\\XIVLauncher\\XIVLauncher.exe", command.FileName);
        Assert.Equal("C:\\Program Files\\XIVLauncher", command.WorkingDirectory);
        Assert.Equal("--account=potato-True-False --Autologinenabled=false", command.Arguments);
    }

    private string CreateBatchFile(params string[] lines)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PotatoLauncherTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        temporaryDirectories.Add(directory);
        var path = Path.Combine(directory, "account.bat");
        File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose()
    {
        foreach (var directory in temporaryDirectories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
