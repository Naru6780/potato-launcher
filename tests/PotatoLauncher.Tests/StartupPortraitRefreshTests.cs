namespace PotatoLauncher.Tests;

public class StartupPortraitRefreshTests
{
    [Fact]
    public void ShouldRefreshPortraitOnStartup_RefreshesLinkedProfiles()
    {
        var profile = new AccountIconProfile { LodestoneId = "34875007" };

        Assert.True(MainForm.ShouldRefreshPortraitOnStartup(profile));
    }

    [Fact]
    public void ShouldRefreshPortraitOnStartup_DoesNotAutoDetectUnlinkedProfiles()
    {
        var profile = new AccountIconProfile
        {
            CharacterName = "Artemis Potato",
            World = "Sargatanas"
        };

        Assert.False(MainForm.ShouldRefreshPortraitOnStartup(profile));
    }

    [Fact]
    public void CanRefreshPortraitManually_AllowsExactNameWorldAutoDetection()
    {
        var profile = new AccountIconProfile
        {
            CharacterName = "Artemis Potato",
            World = "Sargatanas"
        };

        Assert.True(MainForm.CanRefreshPortraitManually(profile));
    }
}
