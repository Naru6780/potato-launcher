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
    public void HelpWindowText_UsesWindowsLineBreaksBetweenSections()
    {
        var text = AppText.HelpWindowText();

        Assert.Contains($"Launch modes{Environment.NewLine}", text);
        Assert.Contains($"{Environment.NewLine}{Environment.NewLine}Accounts", text);
        Assert.DoesNotContain("\nAccounts", text.Replace(Environment.NewLine, ""));
    }
}
