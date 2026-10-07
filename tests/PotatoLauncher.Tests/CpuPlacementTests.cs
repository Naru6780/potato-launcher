namespace PotatoLauncher.Tests;

public class CpuPlacementTests
{
    private const long MB = 1024 * 1024;

    private static List<CpuCore> Cores(int count) => Enumerable.Range(0, count).Select(i => new CpuCore(3L << (i * 2), 0)).ToList();

    // Ryzen 7 9800X3D: one CCD, 8 cores / 16 threads, 96 MB L3.
    private static CpuTopology R9800X3D() => CpuTopology.FromParts(Cores(8), [(0xFFFF, 96 * MB, 3), (0x3, 1 * MB, 2)], 16);

    // Ryzen 9 9950X3D: CCD0 (CPUs 0-15) has the 96 MB V-Cache, CCD1 (16-31) has 32 MB. Listed CCD1 first on purpose.
    private static CpuTopology R9950X3D() => CpuTopology.FromParts(Cores(16), [(unchecked((long)0xFFFF0000), 32 * MB, 3), (0xFFFF, 96 * MB, 3)], 32);

    private static int Bits(long mask) => System.Numerics.BitOperations.PopCount(unchecked((ulong)mask));

    [Fact]
    public void Topology_ReadsCcdsAndPutsTheVCacheCcdFirst()
    {
        var single = R9800X3D();
        Assert.Single(single.Domains);
        Assert.False(single.HasUnequalCores);
        Assert.Contains("every core is equal", single.Describe());

        var dual = R9950X3D();
        Assert.Equal(2, dual.Domains.Count);
        Assert.Equal(0xFFFF, dual.Domains[0].Mask);
        Assert.True(dual.HasUnequalCores);
        Assert.Equal("2 CCDs: 96 MB L3 on CPUs 0-15, 32 MB L3 on CPUs 16-31", dual.Describe());
        Assert.Equal(0xFFFFFFFFL, dual.AllMask);
    }

    [Fact]
    public void Topology_DetectsThisPc()
    {
        var topology = CpuTopology.Detect();
        Assert.True(topology.IsSupported);
        Assert.Equal(Environment.ProcessorCount, topology.Domains.Sum(domain => domain.LogicalCount));
    }

    [Theory]
    [InlineData(0xFFL, 0L, "0-7")]
    [InlineData(0xF0F0L, 0L, "4-7,12-15")]
    [InlineData(0x5L, 0L, "0,2")]
    [InlineData(0xFFFFL, 0xFFFFL, "all")]
    public void FormatMask_WritesRanges(long mask, long all, string expected) => Assert.Equal(expected, CpuTopology.FormatMask(mask, all));

    [Fact]
    public void Off_LeavesEveryClientOnEveryCore()
    {
        var topology = R9950X3D();
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.Off, [1, 2, 3], 1);
        Assert.All(plan.Values, mask => Assert.Equal(topology.AllMask, mask));
    }

    [Fact]
    public void ReserveMain_On9950X3D_MainOwnsVCacheCoresAndNoFollowerStraddlesCcds()
    {
        var topology = R9950X3D();
        var clients = Enumerable.Range(1, 16).ToList();
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, clients, mainId: 1, followerLoad: 15 * 1.0);

        Assert.Equal(0xFFL, plan[1]);
        foreach (var follower in clients.Skip(1))
        {
            Assert.Equal(0, plan[follower] & plan[1]);
            Assert.True(plan[follower] == 0xFF00L || plan[follower] == unchecked((long)0xFFFF0000), $"{follower}: {plan[follower]:X}");
        }
        // Spread by size: the 16-thread CCD takes about twice the followers of the 8 spare V-Cache threads.
        Assert.Equal(5, clients.Skip(1).Count(id => plan[id] == 0xFF00L));
        Assert.Equal(10, clients.Skip(1).Count(id => plan[id] == unchecked((long)0xFFFF0000)));
    }

    [Fact]
    public void ReserveMain_On9800X3D_ShrinksTheMainsCoresWhenFollowersNeedThem()
    {
        var topology = R9800X3D();
        // 8 clients, measured ~0.5 logical CPU per follower: the main keeps 4 cores.
        var eight = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, Enumerable.Range(1, 8).ToList(), 1, followerLoad: 7 * 0.5);
        Assert.Equal(0xFFL, eight[1]);
        Assert.All(Enumerable.Range(2, 7), id => Assert.Equal(0xFF00L, eight[id]));

        // 16 clients at the same cost: followers need ~11 threads, so the main keeps only 2 cores.
        var sixteen = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, Enumerable.Range(1, 16).ToList(), 1, followerLoad: 15 * 0.5);
        Assert.Equal(0xFL, sixteen[1]);
        Assert.All(Enumerable.Range(2, 15), id => Assert.Equal(0xFFF0L, sixteen[id]));

        // Followers that need the whole chip: no reservation at all.
        var heavy = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, Enumerable.Range(1, 16).ToList(), 1, followerLoad: 15 * 1.0);
        Assert.All(heavy.Values, mask => Assert.Equal(0xFFFFL, mask));
    }

    [Fact]
    public void Lanes_AreTwoCoresAndStayInsideOneCcd()
    {
        var topology = R9950X3D();
        var clients = Enumerable.Range(1, 16).ToList();
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.Lanes, clients, mainId: 1, followerLoad: 15 * 0.5);
        foreach (var follower in clients.Skip(1))
        {
            Assert.Equal(4, Bits(plan[follower]));
            var inCcd0 = (plan[follower] & 0xFFFF) == plan[follower];
            var inCcd1 = (plan[follower] & unchecked((long)0xFFFF0000)) == plan[follower];
            Assert.True(inCcd0 ^ inCcd1);
        }
    }

    [Fact]
    public void CacheCcdForMain_GivesTheMainTheWholeVCacheCcdWhileFollowersFitElsewhere()
    {
        var topology = R9950X3D();
        var clients = Enumerable.Range(1, 16).ToList();
        // His measured load: ~2.3% of 32 threads = ~0.74 thread per follower, 11 in total; fits in 16 with 25% spare.
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, clients, mainId: 1, followerLoad: 15 * 0.74);
        Assert.Equal(0xFFFFL, plan[1]);
        Assert.All(clients.Skip(1), id => Assert.Equal(unchecked((long)0xFFFF0000), plan[id]));

        // Followers busier than the other CCD can hold: back to the main's own cores, followers on both CCDs.
        var busy = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, clients, mainId: 1, followerLoad: 15 * 1.0);
        Assert.Equal(0xFFL, busy[1]);
        Assert.Contains(clients.Skip(1), id => busy[id] == 0xFF00L);

        // One CCD (9800X3D): nothing to give, same as Main gets its own cores.
        var single = R9800X3D();
        var ids = Enumerable.Range(1, 8).ToList();
        Assert.Equal(
            CpuPlacementPlanner.Plan(single, CpuPlacementMode.ReserveMain, ids, 1, 7 * 0.5),
            CpuPlacementPlanner.Plan(single, CpuPlacementMode.CacheCcdForMain, ids, 1, 7 * 0.5));
    }

    [Fact]
    public void WithoutAMain_FollowersStillStayInsideOneCcd()
    {
        var plan = CpuPlacementPlanner.Plan(R9950X3D(), CpuPlacementMode.ReserveMain, [1, 2, 3], mainId: null);
        Assert.All(plan.Values, mask => Assert.True(mask == 0xFFFFL || mask == unchecked((long)0xFFFF0000)));
    }

    [Fact]
    public void SingleClient_IsNeverPinned()
    {
        var topology = R9950X3D();
        Assert.Equal(topology.AllMask, CpuPlacementPlanner.Plan(topology, CpuPlacementMode.Lanes, [7], 7)[7]);
    }

    [Fact]
    public void Score_PrefersClientsAtTargetThenMainFpsThenCpu()
    {
        static LoadWindow Window(double cpu, params (int Id, double Fps)[] fps) =>
            new(cpu + 10, cpu, new Dictionary<int, double>(), fps.ToDictionary(entry => entry.Id, entry => entry.Fps));

        // Same FPS everywhere: 3 points less CPU wins (measured on the 9800X3D).
        var cheaper = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(31.2, (1, 49), (2, 59))),
            (CpuPlacementMode.ReserveMain, Window(28.4, (1, 50), (2, 59))),
            (CpuPlacementMode.Lanes, Window(28.4, (1, 50), (2, 59)))], 60, mainId: 1);
        Assert.Equal(CpuPlacementMode.ReserveMain, cheaper.Winner);

        // Less CPU never beats a client dropping below target.
        var starving = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(31, (1, 60), (2, 60))),
            (CpuPlacementMode.Lanes, Window(25, (1, 60), (2, 52)))], 60, mainId: 1);
        Assert.Equal(CpuPlacementMode.Off, starving.Winner);

        // A main that gains 10 FPS wins even at slightly higher CPU.
        var faster = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(30, (1, 47), (2, 60))),
            (CpuPlacementMode.ReserveMain, Window(30.5, (1, 57), (2, 60)))], 60, mainId: 1);
        Assert.Equal(CpuPlacementMode.ReserveMain, faster.Winner);

        // Within the CPU noise and 2 FPS: the simpler placement stays.
        var tie = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(30, (1, 60))),
            (CpuPlacementMode.ReserveMain, Window(28.5, (1, 60)))], 60, mainId: 1);
        Assert.Equal(CpuPlacementMode.Off, tie.Winner);
    }

    [Fact]
    public void Score_UserRunOn9800X3D_ClientsAt58AreAtTargetAndTotalCpuDecides()
    {
        // The real run: every client at ~58 under the game's own limiter; one at 57.9 must not flip the result.
        static LoadWindow Window(double system, double clients, double lowest) =>
            new(system, clients, new Dictionary<int, double>(),
                Enumerable.Range(1, 8).ToDictionary(id => id, id => id == 8 ? lowest : 58.1));
        var result = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(44.9, 29.4, 58.0)),
            (CpuPlacementMode.ReserveMain, Window(40.9, 29.7, 57.9)),
            (CpuPlacementMode.Lanes, Window(39.2, 28.9, 57.9))], 60, mainId: 1);
        Assert.All(result.Scores, score => Assert.Equal(8, score.AtTarget));
        // 4 points below no pinning; lanes are only 1.7 lower again, inside the noise, so the simpler one stays.
        Assert.Equal(CpuPlacementMode.ReserveMain, result.Winner);
    }

    [Fact]
    public void Settings_RoundTripPlacementAndIgnoreOldLaneKeys()
    {
        var settings = OptimizerSettings.DeserializeCompatible("""
            { "cpuPlacement": "Lanes", "testedPlacement": "ReserveMain", "cpuAssignmentMode": "SplitLanes", "mainLogicalProcessors": 6 }
            """);
        settings.Normalize();
        Assert.Equal(CpuPlacementSetting.Lanes, settings.CpuPlacement);
        Assert.Equal(CpuPlacementMode.ReserveMain, settings.TestedPlacement);
        Assert.Equal(CpuPlacementSetting.Auto, new OptimizerSettings().CpuPlacement);
    }
}
