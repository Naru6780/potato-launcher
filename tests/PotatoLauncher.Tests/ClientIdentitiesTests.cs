namespace PotatoLauncher.Tests;

public class ClientIdentitiesTests
{
    [Fact]
    public void LoadingScreenKeepsTheConfirmedCharacterName()
    {
        var loading = new ExternalGameSnapshot(WorldReadiness.Loading, "");
        Assert.Equal("Test Player@Jenova", ClientLabels.Title("account", loading, "Test Player@Jenova"));
        Assert.Contains("Loading", ClientLabels.Title("account", loading));
        // At the title screen / character select the character may change, so the label is not kept there.
        Assert.Contains("Client running", ClientLabels.Title("account", new ExternalGameSnapshot(WorldReadiness.Unknown, ""), "Test Player@Jenova"));
    }

    [Fact]
    public void IdentityIsBoundToProcessStartTime()
    {
        var pid = Random.Shared.Next(2_000_000, 3_000_000);
        var start = DateTime.UtcNow;
        ClientIdentities.Set(pid, start, "Test Player@Jenova");
        Assert.Equal("Test Player@Jenova", ClientIdentities.Get(pid, start));
        // A reused PID (different start time) never inherits another client's identity.
        Assert.Null(ClientIdentities.Get(pid, start.AddSeconds(5)));
        Assert.Null(ClientIdentities.Get(pid, start));
    }
}
