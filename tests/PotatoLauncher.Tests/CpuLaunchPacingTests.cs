namespace PotatoLauncher.Tests;

public class CpuLaunchPacingTests
{
    [Fact]
    public async Task CancelledQueueDoesNotSampleOrReportReady()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CpuLaunchPacing.WaitAsync(80, _ => throw new Exception("Should not run"), cancellation.Token));
    }

    [Fact]
    public async Task NativeCounterProducesARealSampleAndDisposes()
    {
        using var counter = new WindowsCpuCounter();
        await Task.Delay(1100);
        Assert.InRange(counter.NextValue(), 0, 100);
        counter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => counter.NextValue());
    }
    [Fact]
    public void ThreeConsecutiveCalmReadingsRequired()
    {
        var gate = new CpuCalmWindow(80);
        Assert.False(gate.Observe(100));
        Assert.False(gate.Observe(60));
        Assert.False(gate.Observe(70));
        Assert.True(gate.Observe(79));
    }

    [Fact]
    public void SpikeOrThresholdResetsCalmWindow()
    {
        var gate = new CpuCalmWindow(80);
        gate.Observe(40);
        gate.Observe(50);
        Assert.False(gate.Observe(80));
        Assert.Equal(0, gate.CalmReadings);
        Assert.False(gate.Observe(79));
        Assert.False(gate.Observe(100));
        Assert.Equal(0, gate.CalmReadings);
    }

    [Fact]
    public void FailedMeasurementIsNotIdleCpu()
    {
        var gate = new CpuCalmWindow(80);
        foreach (var bad in new double?[] { null, double.NaN, double.PositiveInfinity, -1 })
        {
            gate.Observe(50);
            gate.Observe(50);
            Assert.False(gate.Observe(bad));
            Assert.Equal(0, gate.CalmReadings);
        }
    }
}
