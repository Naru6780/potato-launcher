namespace PotatoLauncher.Tests;

public class AccountSortPolicyTests
{
    private static readonly Account Alpha = new("Alpha", "alpha.bat", 0, "alpha-key");
    private static readonly Account Beta = new("Beta", "beta.bat", 1, "beta-key");
    private static readonly Account Gamma = new("Gamma", "gamma.bat", 2, "gamma-key");

    [Fact]
    public void SelectedBand_PutsMembersFirstInBandOrder()
    {
        var result = Order([Alpha, Beta, Gamma], AccountSortModes.SelectedBand, selectedBandFiles: ["gamma.bat", "alpha.bat"]);

        Assert.Equal([Gamma, Alpha, Beta], result);
    }

    [Fact]
    public void SelectedBand_ChangesImmediatelyWhenSelectedBandChanges()
    {
        var firstBand = Order([Alpha, Beta, Gamma], AccountSortModes.SelectedBand, selectedBandFiles: ["alpha.bat"]);
        var secondBand = Order([Alpha, Beta, Gamma], AccountSortModes.SelectedBand, selectedBandFiles: ["beta.bat"]);

        Assert.Equal([Alpha, Beta, Gamma], firstBand);
        Assert.Equal([Beta, Alpha, Gamma], secondBand);
    }

    [Fact]
    public void LastConnected_RemainsDynamic()
    {
        var connected = new Dictionary<string, DateTime>
        {
            ["alpha-key"] = new DateTime(2026, 1, 1),
            ["beta-key"] = new DateTime(2026, 1, 3),
            ["gamma-key"] = new DateTime(2026, 1, 2)
        };

        var result = AccountSortPolicy.Order(
            [Alpha, Beta, Gamma],
            AccountSortModes.LastConnected,
            [],
            connected,
            null,
            account => account.AccountKey,
            account => account.Name);

        Assert.Equal([Beta, Gamma, Alpha], result);
    }

    [Fact]
    public void CustomOrder_UsesPersistedKeys()
    {
        var result = AccountSortPolicy.Order(
            [Alpha, Beta, Gamma],
            AccountSortModes.Custom,
            ["gamma-key", "alpha-key", "beta-key"],
            new Dictionary<string, DateTime>(),
            null,
            account => account.AccountKey,
            account => account.Name);

        Assert.Equal([Gamma, Alpha, Beta], result);
    }

    private static IReadOnlyList<Account> Order(IReadOnlyList<Account> accounts, string mode, IReadOnlyList<string>? selectedBandFiles)
    {
        return AccountSortPolicy.Order(
            accounts,
            mode,
            [],
            new Dictionary<string, DateTime>(),
            selectedBandFiles,
            account => account.AccountKey,
            account => account.Name);
    }
}
