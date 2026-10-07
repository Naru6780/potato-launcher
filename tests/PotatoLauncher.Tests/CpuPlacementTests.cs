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

        // 16 clients at the same cost: followers need 7.5 x 1.15 = 8.6 threads, so the main keeps 3 cores (10 left).
        var sixteen = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, Enumerable.Range(1, 16).ToList(), 1, followerLoad: 15 * 0.5);
        Assert.Equal(0x3FL, sixteen[1]);
        Assert.All(Enumerable.Range(2, 15), id => Assert.Equal(0xFFC0L, sixteen[id]));

        // Followers that would need the whole chip: the main still keeps its 2-core floor, the followers the other 12 threads.
        var heavy = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, Enumerable.Range(1, 16).ToList(), 1, followerLoad: 15 * 1.0);
        Assert.Equal(0xFL, heavy[1]);
        Assert.All(Enumerable.Range(2, 15), id => Assert.Equal(0xFFF0L, heavy[id]));
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
        // 15 followers at 0.6 thread = 9 in total: fits in the other CCD's 16 threads with 50% spare, so it engages.
        // (At his measured ~11 threads it only stays engaged once in, it does not enter from scratch: see CacheCcd hysteresis.)
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, clients, mainId: 1, followerLoad: 15 * 0.6);
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
    public void MainCores_GiveUpAtOnceAndComeBackOnlyAfterAMinute()
    {
        // The 9800X3D case from 1.0.125's log: 7 followers at ~5.3-5.4 threads flipped the main between 0-7 and 0-5.
        // Now 4 cores leave them 8 threads, enough up to 6.9 threads of load (1.15x); from 7.0 the main drops to 3...
        var topology = R9800X3D();
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 7, 5.4));
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 7, 6.9));
        Assert.Equal(3, CpuPlacementPlanner.MainCoreCount(topology, 7, 7.0));
        // ...unless it already holds 4: those stay until the followers would have less than 1.05x (load over 7.6).
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 7, 7.0, previous: 4));
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 7, 7.6, previous: 4));
        Assert.Equal(3, CpuPlacementPlanner.MainCoreCount(topology, 7, 7.7, previous: 4));
        var t = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);
        DateTime? since = null;
        Assert.Equal(3, CpuPlacementPlanner.WithRegrowDelay(3, previous: 4, t, ref since));                 // give up: immediate
        Assert.Equal(3, CpuPlacementPlanner.WithRegrowDelay(4, previous: 3, t.AddSeconds(1), ref since));  // back under the line: wait
        Assert.Equal(3, CpuPlacementPlanner.WithRegrowDelay(4, previous: 3, t.AddSeconds(40), ref since));
        Assert.Equal(4, CpuPlacementPlanner.WithRegrowDelay(4, previous: 3, t.AddSeconds(62), ref since)); // a full minute: take it back
        Assert.Equal(3, CpuPlacementPlanner.WithRegrowDelay(3, previous: 4, t.AddSeconds(63), ref since));
        Assert.Null(since);
    }

    [Fact]
    public void MainIsNeverSqueezedToOneCore_FriendsRecordedSpike()
    {
        // 9950X3D, 15 followers. 1.0.126 pinned the main to 0-1 at 18.7 threads of follower load (60 -> 35 FPS) and kept it there.
        // 1.0.129 gave it 2 cores (0-3) at 19.7 threads: 41-45 FPS while 8 threads idled. The main comes first: 4 cores
        // leave the followers 24 threads, enough up to 20.8 threads of load (1.15x).
        var topology = R9950X3D();
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 15, 18.7, mainLoad: 2.2));
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 15, 19.7, mainLoad: 2.3)); // recorded 2026-10-07 16:59
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 15, 20.8, mainLoad: 2.3));
        Assert.Equal(3, CpuPlacementPlanner.MainCoreCount(topology, 15, 22.0, mainLoad: 2.4)); // 26 threads left for 25.3 needed
        Assert.Equal(2, CpuPlacementPlanner.MainCoreCount(topology, 15, 25.0, mainLoad: 2.4)); // overloaded: the floor, never one core, never none
        Assert.Equal(2, CpuPlacementPlanner.MainCoreCount(topology, 15, 40.0, mainLoad: 2.4));
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 15, 12.0, mainLoad: 5.0)); // busy main (needs 4) with room: 4
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 15, 25.0, mainLoad: 5.0)); // needs 4: keeps them, main first
        // Holding 4 at a load that would size 3 from scratch: kept while the followers have 1.05x (24 >= 22.86 at 21.77).
        Assert.Equal(4, CpuPlacementPlanner.MainCoreCount(topology, 15, 22.0, mainLoad: 2.4, previous: 4));
        Assert.Equal(3, CpuPlacementPlanner.MainCoreCount(topology, 15, 23.0, mainLoad: 2.4, previous: 4));

        // The plan on his recorded loads: main 0-7, followers on 8-15 and 16-31, never on the main's cores.
        var ids = Enumerable.Range(1, 16).ToList();
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.ReserveMain, ids, 1, 19.7, null, out var state, mainLoad: 2.3);
        Assert.Equal(0xFFL, plan[1]);
        Assert.Equal(4, state.MainCores);
        Assert.All(ids.Skip(1), id => Assert.Equal(0, plan[id] & 0xFF));
        // Overloaded followers still never put the main on one core or on a shared CCD.
        var overloaded = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.Lanes, ids, 1, 25.0, null, out _, mainLoad: 2.4);
        Assert.Equal(0xFL, overloaded[1]);
        Assert.All(ids.Skip(1), id => Assert.Equal(0, overloaded[id] & 0xF));
        for (var cores = 1; cores <= 4; cores++)
            Assert.NotEqual(1, CpuPlacementPlanner.MainCoreCount(topology, 15, 15.0 + cores, mainLoad: 2.0));
    }

    [Fact]
    public void CacheCcd_IsOnlyTestedWhenItCanEngage_AndFallsBackToOwnCores()
    {
        var topology = R9950X3D();
        // 15 followers at 19.7 threads (recorded): 1.5x = 29.6 > 16 threads outside the cache CCD: not a candidate.
        Assert.DoesNotContain(CpuPlacementMode.CacheCcdForMain, CpuPlacementPlanner.TestCandidates(topology, 15, 19.7));
        // 8 followers at 10 threads: 15 <= 16: a candidate.
        Assert.Contains(CpuPlacementMode.CacheCcdForMain, CpuPlacementPlanner.TestCandidates(topology, 8, 10.0));
        // One CCD: never.
        Assert.DoesNotContain(CpuPlacementMode.CacheCcdForMain, CpuPlacementPlanner.TestCandidates(R9800X3D(), 3, 2.0));
        Assert.Equal(3, CpuPlacementPlanner.TestCandidates(R9800X3D(), 3, 2.0).Count);

        // Chosen anyway with 15 followers: it runs as "Main gets its own cores" with 4 cores, and says so in its state.
        var ids = Enumerable.Range(1, 16).ToList();
        var plan = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, ids, 1, 19.7, null, out var state, mainLoad: 2.3);
        Assert.False(state.CacheCcdEngaged);
        Assert.Equal(4, state.MainCores);
        Assert.Equal(0xFFL, plan[1]);
    }
    [Fact]
    public void CacheCcd_DoesNotFlipNearItsThreshold()
    {
        var topology = R9950X3D();
        var ids = Enumerable.Range(1, 16).ToList();
        // 15 followers at 13.5 threads: fits with 25% spare (16.9 > 16? no) -> not engaged from scratch at 1.5x either.
        var engaged = CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, ids, 1, 10.0, previous: null, out var state);
        Assert.True(state.CacheCcdEngaged);
        Assert.Equal(0xFFFFL, engaged[1]);
        // Load rises to 12: still fits at 1.25x (15 <= 16), stays engaged.
        CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, ids, 1, 12.0, state, out state);
        Assert.True(state.CacheCcdEngaged);
        // From scratch, 12 needs 1.5x = 18 > 16: not engaged.
        CpuPlacementPlanner.Plan(topology, CpuPlacementMode.CacheCcdForMain, ids, 1, 12.0, previous: null, out var fresh);
        Assert.False(fresh.CacheCcdEngaged);
    }

    [Fact]
    public void Score_TiesKeepTheDefaultAndOutsideLoadMakesItInconclusive()
    {
        static LoadWindow Window(double system, double other) =>
            new(system, system - 12, new Dictionary<int, double>(), new Dictionary<int, double> { [1] = 58.1, [2] = 58.2 }, other);

        // Within 2 points: the default (main gets its own cores) stays instead of falling back to no pinning.
        var tie = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(59.4, 19)), (CpuPlacementMode.ReserveMain, Window(58.2, 19)), (CpuPlacementMode.Lanes, Window(60.9, 19))],
            60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        Assert.Equal(CpuPlacementMode.ReserveMain, tie.Winner);
        Assert.False(tie.Inconclusive);

        // A background job that moved by 6 points during the test: inconclusive, nothing should be stored.
        var noisy = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(59.4, 22)), (CpuPlacementMode.ReserveMain, Window(54.0, 16)), (CpuPlacementMode.Lanes, Window(60.9, 21))],
            60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        Assert.True(noisy.Inconclusive);
        Assert.StartsWith("Inconclusive", noisy.Summary);
    }

    [Fact]
    public void Test_RejectsAStarvedRunInsteadOfScoringIt()
    {
        var test = new PlacementTest([CpuPlacementMode.Off, CpuPlacementMode.ReserveMain], rounds: 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 60, null);
        var clients = new List<ClientRef> { new(Environment.ProcessId, DateTime.UtcNow) };
        var start = DateTime.UtcNow;
        test.Tick(start, clients);
        test.Tick(start.AddSeconds(1), clients);
        // Potato's timer then stalls for 25 s, as it did with 16 runaway clients at 100% CPU.
        test.Tick(start.AddSeconds(26), clients);
        Assert.True(test.Done);
        Assert.Null(test.Result);
        Assert.Contains("got no CPU time", test.Error);
    }

    [Theory]
    [InlineData(false, false, false, 185.0, true)]   // Frame Rate None, render-cut follower at 185: hold it
    [InlineData(false, false, false, 61.0, false)]   // None but paced by the NVIDIA cap at 60: leave it
    [InlineData(false, true, false, 185.0, false)]   // the client being played is never held
    [InlineData(true, false, false, 60.0, true)]     // once held it stays held (its FPS now sits at the target)
    [InlineData(true, false, true, 58.0, false)]     // in-game limit set: the game takes over
    [InlineData(true, true, false, 60.0, false)]     // became the active client: released
    public void Runaway_DetectionIsStickyAndNeverTouchesTheActiveClient(bool was, bool active, bool hasLimit, double fps, bool expected) =>
        Assert.Equal(expected, RunawayPolicy.IsRunaway(was, active, hasLimit, fps, 60));

    [Fact]
    public void Brake_AppliesToMinimizedOrRunawayClientsWithoutAnInGameLimit()
    {
        Assert.True(RunawayPolicy.ShouldBrake(minimized: true, runaway: false, hasEngineLimit: false));
        Assert.True(RunawayPolicy.ShouldBrake(minimized: false, runaway: true, hasEngineLimit: false));
        Assert.False(RunawayPolicy.ShouldBrake(minimized: true, runaway: true, hasEngineLimit: true));
        Assert.False(RunawayPolicy.ShouldBrake(minimized: false, runaway: false, hasEngineLimit: false));
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
