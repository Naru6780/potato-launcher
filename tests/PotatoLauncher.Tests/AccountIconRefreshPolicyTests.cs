namespace PotatoLauncher.Tests;

public class AccountIconRefreshPolicyTests
{
    [Fact]
    public void NeedsStartupRefresh_SkipsFreshCompleteCache()
    {
        var profile = new AccountIconProfile
        {
            LodestoneId = "12345",
            LastUpdatedUtc = new DateTime(2026, 5, 25, 9, 0, 0, DateTimeKind.Utc)
        };

        var needsRefresh = AccountIconRefreshPolicy.NeedsStartupRefresh(
            profile,
            new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc),
            faceExists: true,
            fullImageExists: true);

        Assert.False(needsRefresh);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NeedsStartupRefresh_RefreshesMissingCachedImages(bool faceExists, bool fullImageExists)
    {
        var profile = new AccountIconProfile
        {
            LodestoneId = "12345",
            LastUpdatedUtc = new DateTime(2026, 5, 25, 9, 0, 0, DateTimeKind.Utc)
        };

        var needsRefresh = AccountIconRefreshPolicy.NeedsStartupRefresh(
            profile,
            new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc),
            faceExists,
            fullImageExists);

        Assert.True(needsRefresh);
    }

    [Fact]
    public void NeedsStartupRefresh_RefreshesStaleCompleteCache()
    {
        var profile = new AccountIconProfile
        {
            LodestoneId = "12345",
            LastUpdatedUtc = new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc)
        };

        var needsRefresh = AccountIconRefreshPolicy.NeedsStartupRefresh(
            profile,
            new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc),
            faceExists: true,
            fullImageExists: true);

        Assert.True(needsRefresh);
    }
}
