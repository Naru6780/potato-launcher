using System.Drawing;

namespace PotatoLauncher.Tests;

public class AppTextTests
{
    [Theory]
    [InlineData(1, "1 account needs a linked Lodestone profile.")]
    [InlineData(3, "3 accounts need linked Lodestone profiles.")]
    public void MissingAccountIconStatus_AsksForLinkedLodestoneProfiles(int missingCount, string expected)
    {
        Assert.Equal(expected, AppText.MissingAccountIconStatus(missingCount));
    }

    [Fact]
    public void LodestoneCharacterSearchUrl_PointsToNorthAmericanCharacterSearch()
    {
        Assert.Equal("https://na.finalfantasyxiv.com/lodestone/character/", AppText.LodestoneCharacterSearchUrl);
    }

    [Fact]
    public void LodestoneHelperLinkText_IsShortEnoughForPrompt()
    {
        Assert.Equal("Open Lodestone character search", AppText.LodestoneHelperLinkText);
    }

    [Theory]
    [InlineData("1.0.64", "Potato Launcher v1.0.64")]
    [InlineData("", "Potato Launcher")]
    [InlineData("   ", "Potato Launcher")]
    public void WindowTitle_IncludesVersionWhenAvailable(string version, string expected)
    {
        Assert.Equal(expected, AppText.WindowTitle(version));
    }

    [Fact]
    public void HelpWindowText_UsesWindowsLineBreaksBetweenSections()
    {
        var text = AppText.HelpWindowText();

        Assert.Contains($"Launch modes{Environment.NewLine}", text);
        Assert.Contains($"{Environment.NewLine}{Environment.NewLine}Accounts", text);
        Assert.DoesNotContain("\nAccounts", text.Replace(Environment.NewLine, ""));
    }

    [Fact]
    public void LoadingQueueText_UsesFriendlyAccountNameAndStatus()
    {
        var account = new Account("musicapotato17 - Hermes Potato", "02-Hermes.bat", 2, "musicapotato17-False-False");

        Assert.Equal("Hermes Potato - Initialized", MainForm.LoadingQueueText(account, "Initialized"));
    }

    [Theory]
    [InlineData("Queued", "Queued")]
    [InlineData("Launching", "Loading")]
    [InlineData("Connecting", "Loading")]
    [InlineData("Initializing (2/3)", "Loading")]
    [InlineData("Initialized", "Initialized")]
    public void NormalizeLoadingQueueState_UsesOnlyUserFacingQueueStates(string input, string expected)
    {
        Assert.Equal(expected, MainForm.NormalizeLoadingQueueState(input));
    }

    [Fact]
    public void LoadingQueueStateColor_UsesWhiteBlueAndGreenForQueueProgress()
    {
        var palette = new ThemePalette(default, default, default, default, default, default, default, default, Color.Red, default);

        Assert.Equal(Color.White.ToArgb(), MainForm.LoadingQueueStateColor(palette, "Queued").ToArgb());
        Assert.Equal(Color.FromArgb(105, 172, 255).ToArgb(), MainForm.LoadingQueueStateColor(palette, "Loading").ToArgb());
        Assert.Equal(Color.FromArgb(98, 214, 135).ToArgb(), MainForm.LoadingQueueStateColor(palette, "Initialized").ToArgb());
        Assert.Equal(Color.Red.ToArgb(), MainForm.LoadingQueueStateColor(palette, "Cancelled").ToArgb());
    }
}
