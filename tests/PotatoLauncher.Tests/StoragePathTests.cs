namespace PotatoLauncher.Tests;

public class StoragePathTests
{
    [Fact]
    public void PersistentDataRoot_UsesRoamingAppDataPotatoLauncherFolder()
    {
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Potato Launcher");

        Assert.Equal(expectedRoot, MainForm.PersistentDataRoot());
    }

    [Fact]
    public void PersistentFiles_LiveUnderPersistentDataRoot()
    {
        var root = MainForm.PersistentDataRoot();

        Assert.Equal(Path.Combine(root, "settings.json"), MainForm.SettingsPath());
        Assert.Equal(Path.Combine(root, "accountList.json"), MainForm.AccountListStatePath());
        Assert.Equal(Path.Combine(root, "optimizer.json"), MainForm.OptimizerSettingsPath());
        Assert.Equal(Path.Combine(root, "band.json"), MainForm.BandExportPath());
        Assert.Equal(Path.Combine(root, "Account Icons"), MainForm.AccountIconsFolder());
    }

    [Fact]
    public void PersistentDataRoot_CanBeIsolatedForTestBuilds()
    {
        var isolatedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "potato-launcher-test-profile"));
        try
        {
            RuntimeOptions.Configure(["--data-dir", isolatedRoot]);
            Assert.Equal(isolatedRoot, MainForm.PersistentDataRoot());
            Assert.Equal(Path.Combine(isolatedRoot, "optimizer.json"), MainForm.OptimizerSettingsPath());
        }
        finally
        {
            RuntimeOptions.Configure([]);
        }
    }
}
