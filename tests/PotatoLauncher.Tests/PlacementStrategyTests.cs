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

                // The main always has at least 2 full cores to itself, or shares a whole CCD: never one starved core.
                var main = plan[1];
                var shared = ids.Skip(1).Any(id => (plan[id] & main) != 0);
                Assert.True(shared ? main == topology.Domains[0].Mask || main == topology.AllMask : Bits(main) >= 4,
                    $"load {load:0.0}, main {CpuTopology.FormatMask(main)}, shared {shared}");

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
