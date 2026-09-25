using System.Buffers.Binary;
using System.IO;

namespace PotatoLauncher.Tests;

public class OptimizerSafetyTests
{
    private static long[] Cores(int count, bool smt = true) => Enumerable.Range(0, count)
        .Select(i => smt ? 3L << (i * 2) : 1L << i).ToArray();

    [Theory]
    [InlineData(2, false, 1)]
    [InlineData(4, true, 16)]
    [InlineData(8, true, 16)]
    [InlineData(16, true, 32)]
    [InlineData(32, true, 64)]
    public void SharedPlanUsesCompleteCpuWithNoDomainInformation(int cores, bool smt, int clients)
    {
        var topology = Cores(cores, smt);
        var plan = BalancedCpuPlan.Create(Enumerable.Range(1, clients).ToArray(), topology, []);
        Assert.Equal(clients, plan.Count);
        Assert.All(plan.Values, mask => Assert.Equal(topology.Aggregate(0L, (x, y) => x | y), mask));
    }

    [Fact]
    public void SymmetricDomainsDistributeSixteenClientsEvenly()
    {
        var plan = BalancedCpuPlan.Create(Enumerable.Range(1, 16).ToArray(), Cores(8),
            [new(0xFF, 32, 3), new(0xFF00, 32, 3)]);
        Assert.Equal(8, plan.Values.Count(mask => mask == 0xFF));
        Assert.Equal(8, plan.Values.Count(mask => mask == 0xFF00));
        Assert.Equal(0xFF, plan[1]);
        Assert.Equal(0xFF00, plan[2]);
    }

    [Theory]
    [InlineData(96, false, 16)] // Asymmetric X3D caches.
    [InlineData(32, true, 16)] // Hybrid even with SMT disabled everywhere.
    [InlineData(32, false, 1)] // Light workload: avoid unnecessary restrictions.
    public void AsymmetricHybridOrLightWorkloadsRetainAllCores(int cacheSize, bool hybrid, int count)
    {
        var plan = BalancedCpuPlan.Create(Enumerable.Range(1, count).ToArray(), Cores(8),
            [new(0xFF, cacheSize, 3), new(0xFF00, 32, 3)], hybrid);
        Assert.All(plan.Values, mask => Assert.Equal(0xFFFF, mask));
    }

    [Theory]
    [InlineData(0xFF, 0xFF0)] // Overlap and incomplete coverage.
    [InlineData(0xFF, 0)] // Missing domain.
    [InlineData(1, 0xFFFE)] // Splits a physical core.
    public void InvalidDomainsCannotStrandClientsOnPartialCpu(long first, long second)
    {
        var plan = BalancedCpuPlan.Create(Enumerable.Range(1, 16).ToArray(), Cores(8), [new(first, 32, 3), new(second, 32, 3)]);
        Assert.All(plan.Values, mask => Assert.Equal(0xFFFF, mask));
    }

    [Fact]
    public void UnknownTopologyDoesNotInventSmtPairs()
    {
        Assert.Equal(new long[] { 1, 2, 4, 8, 16 }, ProcessorTopology.CreateFallbackLogicalCores(5));
    }

    [Fact]
    public void EfficiencyClassesDetectHybridCpusAndRejectTruncatedData()
    {
        var records = new byte[96];
        BinaryPrimitives.WriteInt32LittleEndian(records.AsSpan(4), 48);
        BinaryPrimitives.WriteInt32LittleEndian(records.AsSpan(52), 48);
        Assert.False(ProcessorTopology.HasHybridOrUnknownCoreTypesIn(records));
        records[57] = 1;
        Assert.True(ProcessorTopology.HasHybridOrUnknownCoreTypesIn(records));
        Assert.True(ProcessorTopology.HasHybridOrUnknownCoreTypesIn(records.AsSpan(0, 10)));
        Assert.True(ProcessorTopology.HasHybridOrUnknownCoreTypesIn([]));
    }

    [Fact]
    public void FreshSettingsPreferSharedCpuWithoutAutomaticRamEviction()
    {
        var settings = new OptimizerSettings();
        Assert.Equal(CpuAssignmentMode.BalancedShared, settings.CpuAssignmentMode);
        Assert.False(settings.WorkingSetTrimEnabled);
        Assert.False(settings.OptimizerEnabled);
    }

    private sealed class FakeTarget : IAffinityTarget
    {
        public int Id => 42;
        public DateTime StartUtc { get; set; } = new(2026, 1, 1);
        private long mask = 0xFF;
        public bool FailWrites { get; set; }
        public int Writes { get; private set; }
        public long Mask { get => mask; set { if (FailWrites) throw new UnauthorizedAccessException(); mask = value; Writes++; } }
        public void Dispose() { }
    }

    [Fact]
    public void RestoreUsesOriginalNotFullMaskAcrossMultipleApplies()
    {
        var target = new FakeTarget { Mask = 0xAA };
        var session = new AffinitySession(_ => target);
        Assert.True(session.Apply(target, 0xF, out _));
        Assert.True(session.Apply(target, 0x3, out _));
        Assert.Contains("Restored 1", session.Restore());
        Assert.Equal(0xAA, target.Mask);
        Assert.Contains("Restored 0", session.Restore());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestoreDoesNotOverwriteOtherToolsOrReusedPids(bool reuse)
    {
        var target = new FakeTarget();
        var session = new AffinitySession(_ => target);
        session.Apply(target, 3, out _);
        if (reuse) target.StartUtc = target.StartUtc.AddDays(1);
        else target.Mask = 15;
        var expected = target.Mask;
        session.Restore();
        Assert.Equal(expected, target.Mask);
    }

    [Fact]
    public void FailedWritesAreReportedAndFailedRestoresCanBeRetried()
    {
        var target = new FakeTarget { FailWrites = true };
        var session = new AffinitySession(_ => target);
        Assert.False(session.Apply(target, 3, out var error));
        Assert.Contains("42", error);
        target.FailWrites = false;
        session.Apply(target, 3, out _);
        target.FailWrites = true;
        Assert.Contains("1 failed", session.Restore());
        target.FailWrites = false;
        Assert.Contains("Restored 1", session.Restore());
        Assert.Equal(0xFF, target.Mask);
    }

    [Fact]
    public void UnchangedMaskIsNotWrittenOrClaimedByOptimizer()
    {
        var target = new FakeTarget();
        var session = new AffinitySession(_ => throw new Exception("Must not open an unmodified process"));
        Assert.True(session.Apply(target, 0xFF, out _));
        Assert.Equal(0, target.Writes);
        Assert.Contains("Restored 0", session.Restore());
        Assert.False(session.Apply(target, 0, out _));
    }

    [Fact]
    public void GpuUsageSumsProcessesOnSameEngineButNotIndependentEngines()
    {
        var result = GpuEngineUsage.Aggregate([new(1, "adapterA_engine0", 30), new(2, "adapterA_engine0", 40),
            new(1, "adapterA_engine1", 20), new(3, "adapterB_engine0", 50), new(4, "invalid", double.NaN)]);
        Assert.Equal(70, result.Total);
        Assert.Equal(30, result.ByProcess[1]);
        Assert.False(result.ByProcess.ContainsKey(4));
        Assert.Equal(0, GpuEngineUsage.Aggregate([]).Total);
    }

    [Fact]
    public void FpsCaptureUsesPrimarySwapchainAndIncludesSlowFrames()
    {
        using var reader = new StringReader("ProcessID,SwapChainAddress,MsBetweenPresents,Application\n" +
            "1,A,10,\"ffxiv,dx11\"\n1,A,10,ffxiv\n1,A,40,ffxiv\n1,B,1,ffxiv\n" +
            "2,C,20,ffxiv\n2,C,NaN,ffxiv\n2,C,0,ffxiv\n2,C,-1,ffxiv\n");
        var samples = FpsBenchmark.Read(reader);
        Assert.Equal(2, samples.Count);
        Assert.Equal(50, samples[0].AverageFps);
        Assert.Equal(40, samples[0].P95FrameMs);
        Assert.Equal(3, samples[0].Frames);
        Assert.Equal(.06, samples[0].Seconds, 6);
        Assert.Equal(50, samples[1].AverageFps);
    }

    [Fact]
    public void FpsCaptureMissingMetricsFailsInsteadOfReportingZeroFps()
    {
        using var reader = new StringReader("Application,ProcessID\nffxiv,1\n");
        Assert.Throws<IOException>(() => FpsBenchmark.Read(reader));
    }

    [Fact]
    public void AtomicSettingsWriteReplacesContentsWithoutLeavingTemporaryFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PotatoAtomicTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            AtomicTextFile.Write(path, "{\"before\":true}");
            AtomicTextFile.Write(path, "{\"after\":true}");
            Assert.Equal("{\"after\":true}", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
