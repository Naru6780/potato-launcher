using System.Diagnostics;

namespace PotatoLauncher.Tests;

public class ExternalGameStateTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void RequiresLoadedZoneAndNoTransition()
    {
        Assert.True(ExternalGameState.IsLoaded(true, 2, 132, false, false, false, false));
        Assert.False(ExternalGameState.IsLoaded(false, 2, 132, false, false, false, false));
        Assert.False(ExternalGameState.IsLoaded(true, 1, 132, false, false, false, false));
        Assert.False(ExternalGameState.IsLoaded(true, 2, 0, false, false, false, false));
        Assert.False(ExternalGameState.IsLoaded(true, 2, 132, true, false, false, false));
        Assert.False(ExternalGameState.IsLoaded(true, 2, 132, false, true, false, false));
        Assert.False(ExternalGameState.IsLoaded(true, 2, 132, false, false, true, false));
        Assert.False(ExternalGameState.IsLoaded(true, 2, 132, false, false, false, true));
    }

    [Fact]
    public void MissingAndAmbiguousSignaturesFailClosed()
    {
        Assert.Equal(1, ExternalGameState.FindUnique([0, 1, 7, 3], "01 ?? 03"));
        Assert.Throws<IOException>(() => ExternalGameState.FindUnique([0, 1], "02"));
        Assert.Throws<IOException>(() => ExternalGameState.FindUnique([1, 1], "01"));
    }

    [Fact]
    public void ReadinessRequiresStableIdentityAndResetsOnUnknown()
    {
        var gate = new WorldReadinessGate();
        var now = DateTime.UtcNow;
        var value = new ExternalGameSnapshot(WorldReadiness.InWorld, "", "Test Player", 12, 132);
        Assert.False(gate.Observe(value, now));
        Assert.False(gate.Observe(value, now.AddSeconds(2)));
        Assert.True(gate.Observe(value, now.AddSeconds(3)));
        Assert.False(gate.Observe(value with { TerritoryId = 133 }, now.AddSeconds(4)));
        Assert.False(gate.Observe(new(WorldReadiness.Unknown, "access denied"), now.AddSeconds(9)));
        Assert.False(gate.Observe(value, now.AddSeconds(10)));
    }

    [Theory]
    [InlineData("Mutant", "\\BaseNamedObjects\\6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00", true)]
    [InlineData("Mutant", "\\Sessions\\1\\BaseNamedObjects\\6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game01", true)]
    [InlineData("Mutant", "\\Sessions\\2\\BaseNamedObjects\\6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00", false)]
    [InlineData("File", "\\BaseNamedObjects\\6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00", false)]
    [InlineData("Mutant", "\\BaseNamedObjects\\6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00_other", false)]
    [InlineData("Mutant", "\\BaseNamedObjects\\OtherApplication", false)]
    public void OnlyExactGameMutexNamesInThisSessionAreEligible(string type, string name, bool expected)
    {
        Assert.Equal(expected, GameClientLimit.IsInstanceMutex(type, name, 1));
    }

    [Fact]
    public void AccountLabelNeverClaimsCharacterIdentity()
    {
        var unknown = new ExternalGameSnapshot(WorldReadiness.Unknown, "");
        Assert.DoesNotContain("@", ClientLabels.Title("account@example.com", unknown));
        Assert.Contains("Client running", ClientLabels.Title("account", unknown));
        Assert.Equal("Test Player@Jenova", ClientLabels.Title("account", new(WorldReadiness.InWorld, "", "Test Player", 12, 132, 40)));
        Assert.DoesNotContain("@", ClientLabels.Title("account", new(WorldReadiness.Loading, "", "Test Player", 12, 132, 40)));
    }

    [Fact]
    public void MultipleNewClientsAreNeverArbitrarilyAssigned()
    {
        Assert.Null(MainForm.SelectUnambiguousNewClient([]));
        var client = new GameClientWindow(1, IntPtr.Zero, "FINAL FANTASY XIV");
        Assert.Equal(client, MainForm.SelectUnambiguousNewClient([client]));
        Assert.Throws<InvalidOperationException>(() => MainForm.SelectUnambiguousNewClient([client, client with { ProcessId = 2 }]));
    }

    [Fact]
    public void ReadOnlyMutexInspectionWhenExplicitlyRequested()
    {
        if (Environment.GetEnvironmentVariable("POTATO_READ_ONLY_PROBE") != "1") return;
        foreach (var process in Process.GetProcessesByName("ffxiv_dx11"))
        {
            using (process)
            {
                var matches = GameClientLimit.InspectOrRelease(process.Id, process.StartTime.ToUniversalTime(), release: false);
                output.WriteLine($"PID {process.Id}: {matches} exact instance mutex(es); no handles closed.");
            }
        }
    }

    [Fact]
    public void LiveReadOnlyProbeWhenExplicitlyRequested()
    {
        if (Environment.GetEnvironmentVariable("POTATO_READ_ONLY_PROBE") != "1") return;
        var reader = new ExternalGameState();
        foreach (var process in Process.GetProcessesByName("ffxiv_dx11"))
        {
            using (process)
            {
                var result = reader.Read(process.Id, process.StartTime.ToUniversalTime());
                // Intentionally omit character/account names and content IDs from diagnostic output.
                output.WriteLine($"PID {process.Id}: {result.State}; territory={result.TerritoryId}; {result.Detail}");
            }
        }
    }
}
