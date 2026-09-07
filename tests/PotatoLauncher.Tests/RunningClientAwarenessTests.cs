namespace PotatoLauncher.Tests;

public class RunningClientAwarenessTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly DateTime StartedAt = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Account Astraea = new("Astraea Potato", "astraea.bat", 0, "astraea");
    private static RunningGameClient Client(int pid = 10, string title = "Astraea Potato@Jenova") => new(pid, StartedAt, title);

    [Theory]
    [InlineData("Astraea Potato@Jenova", true)]
    [InlineData(" astraea potato @ Jenova ", true)]
    [InlineData("Astraea Potato Extra@Jenova", false)]
    [InlineData("Not Astraea Potato@Jenova", false)]
    [InlineData("FINAL FANTASY XIV", false)]
    [InlineData("Astraea Potato@", false)]
    [InlineData("Astraea Potato@   ", false)]
    [InlineData("@Jenova", false)]
    public void RequiresExactCharacterAndNonemptyWorld(string title, bool expected)
    {
        Assert.Equal(expected, RunningClientAwareness.MatchesName(title, "Astraea Potato"));
    }

    [Fact]
    public async Task DiscoversClientStartedBeforeLauncherWithoutLaunchingOrChangingIt()
    {
        var client = Client();
        var awareness = new RunningClientAwareness(() => [client]);
        var result = await Check(awareness);
        Assert.Same(client, result.ExistingClient);
        Assert.Null(result.StartedClient);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public async Task BandLaunchesOnlyMissingMembersInOrder(int size)
    {
        var clients = new List<RunningGameClient> { Client() };
        var awareness = new RunningClientAwareness(() => clients.ToArray());
        var band = Enumerable.Range(0, size).Select(i => new Account(
            i == 2 ? Astraea.Name : $"Member {i}", $"{i}.bat", i, $"{i}")).ToArray();
        var launched = new List<Account>();
        foreach (var account in band)
        {
            await awareness.LaunchIfNeededAsync(account, account.AccountKey, account.Name, "Jenova", (item, _) =>
            {
                launched.Add(item);
                clients.Add(Client(100 + item.SortOrder, $"{item.Name}@Jenova"));
                return Task.FromResult(new StartedGameClient(item, 100 + item.SortOrder, null));
            }, CancellationToken.None);
        }
        Assert.Equal(band.Where(account => account.Name != Astraea.Name), launched);
        Assert.Equal(size, clients.Count);
        foreach (var account in band)
            await awareness.LaunchIfNeededAsync(account, account.AccountKey, account.Name, "Jenova", NeverLaunch, CancellationToken.None);
    }

    [Fact]
    public async Task RefreshesDiscoveryBetweenMembersAndAfterClientExit()
    {
        var clients = new List<RunningGameClient>();
        var awareness = new RunningClientAwareness(() => clients.ToArray());
        clients.Add(Client());
        Assert.NotNull((await Check(awareness)).ExistingClient);
        clients.Clear();
        Assert.NotNull((await Check(awareness, Launch)).StartedClient);
    }

    [Fact]
    public async Task TrackedLoadingClientIsSkippedWithoutCharacterTitle()
    {
        var awareness = new RunningClientAwareness(() => [Client(title: "FINAL FANTASY XIV")]);
        awareness.Track("ASTRAEA", 10);
        Assert.NotNull((await Check(awareness)).ExistingClient);
    }

    [Fact]
    public async Task ReusedPidDoesNotSuppressLaunch()
    {
        var current = Client();
        var awareness = new RunningClientAwareness(() => [current]);
        awareness.Track("astraea", 10);
        current = current with { StartTimeUtc = StartedAt.AddMinutes(5), Title = "Another Player@Jenova" };
        Assert.NotNull((await Check(awareness, Launch)).StartedClient);
    }

    [Fact]
    public async Task InaccessibleStartTimeAllowsTitleMatchButNotPidOnlyMatch()
    {
        var current = Client() with { StartTimeUtc = null };
        var awareness = new RunningClientAwareness(() => [current]);
        Assert.NotNull((await Check(awareness)).ExistingClient);
        current = current with { Title = "FINAL FANTASY XIV" };
        Assert.NotNull((await Check(awareness, Launch)).StartedClient);
    }

    [Fact]
    public async Task MatchesCorrectWorldAmongSameNamedCharacters()
    {
        var awareness = new RunningClientAwareness(() => [Client(10, "Astraea Potato@Sargatanas"), Client(20)]);
        Assert.Equal(20, (await Check(awareness)).ExistingClient!.ProcessId);
    }

    [Fact]
    public async Task WorldMismatchStopsInsteadOfStartingPotentialDuplicate()
    {
        var awareness = new RunningClientAwareness(() => [Client(title: "Astraea Potato@Sargatanas")]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Check(awareness));
        Assert.Contains("different world", error.Message);
    }

    [Fact]
    public async Task UnknownWorldMatchesUniqueNameButRejectsAmbiguousNames()
    {
        var clients = new List<RunningGameClient> { Client() };
        var awareness = new RunningClientAwareness(() => clients.ToArray());
        Assert.NotNull((await awareness.LaunchIfNeededAsync(Astraea, "first", Astraea.Name, "", NeverLaunch, CancellationToken.None)).ExistingClient);
        clients.Add(Client(20, "Astraea Potato@Sargatanas"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => awareness.LaunchIfNeededAsync(
            Astraea, "second", Astraea.Name, "", NeverLaunch, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownGenericClientIsNotAssignedToAnArbitraryAccount()
    {
        var awareness = new RunningClientAwareness(() => [Client(title: "FINAL FANTASY XIV")]);
        Assert.NotNull((await Check(awareness, Launch)).StartedClient);
    }

    [Fact]
    public async Task CancelledQueueDoesNotEvenScanOrLaunch()
    {
        var awareness = new RunningClientAwareness(() => throw new Exception("Must not scan"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => awareness.LaunchIfNeededAsync(
            Astraea, "astraea", Astraea.Name, "Jenova", NeverLaunch, new CancellationToken(true)));
    }

    [Fact]
    public async Task FailedLaunchCanBeRetried()
    {
        var awareness = new RunningClientAwareness(() => []);
        await Assert.ThrowsAsync<IOException>(() => Check(awareness, (_, _) => throw new IOException("launch failed")));
        Assert.NotNull((await Check(awareness, Launch)).StartedClient);
    }

    [Fact]
    public async Task LiveProcessDiscoveryDryRunNeverStartsClients()
    {
        // Read-only integration check on clients currently on the test machine.
        // No credentials, game launches, window input, or process modifications.
        var snapshot = RunningClientAwareness.Capture();
        output.WriteLine($"Live FFXIV processes discovered: {snapshot.Count}");
        foreach (var client in snapshot.Where(client => client.Title.Contains('@')))
        {
            var separator = client.Title.LastIndexOf('@');
            var name = client.Title[..separator].Trim();
            var world = client.Title[(separator + 1)..].Trim();
            var awareness = new RunningClientAwareness(() => snapshot);
            var result = await awareness.LaunchIfNeededAsync(Astraea, "dry-run", name, world, NeverLaunch, CancellationToken.None);
            Assert.NotNull(result.ExistingClient);
            output.WriteLine($"Dry-run skipped PID {result.ExistingClient.ProcessId}: {client.Title}; launch callback was not invoked.");
        }
    }

    [Fact]
    public void AlreadyRunningQueueRowIsNotDisplayedAsLoading()
    {
        Assert.Equal("Already running", MainForm.NormalizeLoadingQueueState("already running"));
        Assert.Equal("Astraea Potato - Already running", MainForm.LoadingQueueText(Astraea, "Already running"));
    }

    private static Task<BandClientLaunchResult> Check(RunningClientAwareness awareness,
        Func<Account, CancellationToken, Task<StartedGameClient>>? launch = null) =>
        awareness.LaunchIfNeededAsync(Astraea, "astraea", Astraea.Name, "Jenova", launch ?? NeverLaunch, CancellationToken.None);

    private static Task<StartedGameClient> NeverLaunch(Account _, CancellationToken __) =>
        throw new Xunit.Sdk.XunitException("An existing client must not invoke the launch callback.");

    private static Task<StartedGameClient> Launch(Account account, CancellationToken _) =>
        Task.FromResult(new StartedGameClient(account, 99, null));
}
