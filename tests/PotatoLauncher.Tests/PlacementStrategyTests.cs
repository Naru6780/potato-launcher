namespace PotatoLauncher.Tests;

// Checks every strategy against the goal: every client steady at the target, the main above all.
public class PlacementStrategyTests
{
    private const long MB = 1024 * 1024;
    private static List<CpuCore> Cores(int count) => Enumerable.Range(0, count).Select(i => new CpuCore(3L << (i * 2), 0)).ToList();
    private static CpuTopology Single() => CpuTopology.FromParts(Cores(8), [(0xFFFF, 96 * MB, 3)], 16);
    private static CpuTopology Dual() => CpuTopology.FromParts(Cores(16), [(0xFFFF, 96 * MB, 3), (unchecked((long)0xFFFF0000), 32 * MB, 3)], 32);
    private static int Bits(long mask) => System.Numerics.BitOperations.PopCount(unchecked((ulong)mask));

    public static IEnumerable<object[]> Cases()
    {
        foreach (var topology in new[] { "single", "dual" })
        foreach (var mode in Enum.GetValues<CpuPlacementMode>())
        foreach (var clients in new[] { 2, 4, 8, 12, 16 })
            yield return [topology, (int)mode, clients];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryStrategy_KeepsItsPromisesAtEveryLoad(string topologyName, int modeValue, int clients)
    {
        var topology = topologyName == "single" ? Single() : Dual();
        var mode = (CpuPlacementMode)modeValue;
        var ids = Enumerable.Range(1, clients).ToList();
        for (var perFollower = 0.0; perFollower <= 2.0; perFollower += 0.1)
        {
            var load = perFollower * (clients - 1);
            foreach (var mainLoad in new[] { 0.5, 2.0, 5.0 })
            {
                var plan = CpuPlacementPlanner.Plan(topology, mode, ids, mainId: 1, load, null, out _, mainLoad: mainLoad);
                Assert.Equal(clients, plan.Count);
                Assert.All(plan.Values, mask => { Assert.NotEqual(0, mask); Assert.Equal(mask, mask & topology.AllMask); });
                if (mode == CpuPlacementMode.Off) { Assert.All(plan.Values, mask => Assert.Equal(topology.AllMask, mask)); continue; }

                // The main always has 2-4 full cores to itself on the fastest CCD; no follower ever shares them.
                var main = plan[1];
                var shared = ids.Skip(1).Any(id => (plan[id] & main) != 0);
                Assert.False(shared, $"load {load:0.0}, main {CpuTopology.FormatMask(main)} shared with a follower");
                Assert.Equal(main, main & topology.Domains[0].Mask);
                // The cache-CCD mode engaged: the main owns the whole fastest CCD. Otherwise 2-4 cores: 4 whenever the
                // followers fit on the rest at 1.15x, never fewer than 2 however busy they are.
                var wholeCcd = mode == CpuPlacementMode.CacheCcdForMain && main == topology.Domains[0].Mask;
                if (!wholeCcd)
                {
                    Assert.InRange(Bits(main), 4, 8);
                    if (load * CpuPlacementPlanner.FollowerHeadroom <= topology.LogicalCount - 8) Assert.Equal(8, Bits(main));
                }

                // No follower straddles CCDs (crossing them costs more than sharing cores).
                foreach (var id in ids.Skip(1))
                    Assert.True(topology.Domains.Any(domain => (plan[id] & domain.Mask) == plan[id]), $"follower {id} straddles: {CpuTopology.FormatMask(plan[id])}");
            }
        }
    }

    [Fact]
    public void Score_ASteadyPlacementBeatsACheaperOneThatDips()
    {
        static LoadWindow Window(double cpu, params (int Id, double[] Fps)[] clients) =>
            new(cpu + 10, cpu, new Dictionary<int, double>(), clients.ToDictionary(c => c.Id, c => c.Fps.Average()), 0,
                clients.ToDictionary(c => c.Id, c => (IReadOnlyList<double>)c.Fps));
        var steady = Enumerable.Repeat(58.2, 20).ToArray();
        var dipping = Enumerable.Repeat(59.0, 17).Concat([50.0, 49.0, 51.0]).ToArray(); // averages 57.9: looks fine on average
        var result = PlacementTest.Score([
            (CpuPlacementMode.ReserveMain, Window(30, (1, steady), (2, steady))),
            (CpuPlacementMode.Lanes, Window(26, (1, steady), (2, dipping)))], 60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        Assert.Equal(CpuPlacementMode.ReserveMain, result.Winner);
        Assert.Equal(1, result.Scores.Single(score => score.Mode == CpuPlacementMode.Lanes).AtTarget);
    }

    [Fact]
    public void Score_TheMainDecidesFirst_FriendsRecordedRun()
    {
        static LoadWindow Window(double cpu, double main, int atTarget) =>
            new(cpu, cpu - 12, new Dictionary<int, double>(),
                Enumerable.Range(1, 16).ToDictionary(id => id, id => id == 1 ? main : id <= 1 + atTarget ? 58.0 : 50.0), 0);
        // 2026-10-07 on a 9950X3D: no placement held the main at 60. 1.0.129 picked "4/16 at 60, main 43" over "main 45".
        var result = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(92.0, 36, 0)),
            (CpuPlacementMode.ReserveMain, Window(81.6, 44, 0)),
            (CpuPlacementMode.Lanes, Window(77.3, 45, 0)),
            (CpuPlacementMode.CacheCcdForMain, Window(81.1, 43, 4))], 60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        // 45 vs 44 is within noise, so the 4 points less CPU decide for lanes; both beat 43 with four followers at target,
        // and 36 never comes close.
        Assert.Equal(CpuPlacementMode.Lanes, result.Winner);
        // A main steady at target beats one that is not, whatever the followers do.
        var held = PlacementTest.Score([
            (CpuPlacementMode.ReserveMain, Window(85.0, 58, 2)),
            (CpuPlacementMode.Lanes, Window(70.0, 54, 15))], 60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        Assert.Equal(CpuPlacementMode.ReserveMain, held.Winner);
        // Both mains at target: the followers decide.
        var both = PlacementTest.Score([
            (CpuPlacementMode.ReserveMain, Window(85.0, 58, 2)),
            (CpuPlacementMode.Lanes, Window(84.0, 57.5, 15))], 60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        Assert.Equal(CpuPlacementMode.Lanes, both.Winner);
    }

    [Fact]
    public void Score_TheGameGettingBusierMidTestMakesItInconclusive()
    {
        static LoadWindow Window(double clients) => new(clients + 11, clients, new Dictionary<int, double>(), new Dictionary<int, double> { [1] = 58 }, 11);
        var result = PlacementTest.Score([
            (CpuPlacementMode.Off, Window(43)), (CpuPlacementMode.ReserveMain, Window(42)),
            (CpuPlacementMode.ReserveMain, Window(57)), (CpuPlacementMode.Off, Window(64))], 60, mainId: 1, preferred: CpuPlacementMode.ReserveMain);
        Assert.True(result.Inconclusive);
        Assert.Contains("got busier or quieter", result.Summary);
    }
}
