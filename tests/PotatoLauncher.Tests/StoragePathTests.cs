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
        Assert.Equal(Path.Combine(root, "band.json"), MainForm.BandExportPath());
        Assert.Equal(Path.Combine(root, "Account Icons"), MainForm.AccountIconsFolder());
    }
}
