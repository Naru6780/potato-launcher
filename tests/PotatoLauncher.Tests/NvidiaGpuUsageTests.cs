namespace PotatoLauncher.Tests;

public class NvidiaGpuUsageTests
{
    // On a PC with an NVIDIA driver this returns the driver's load; elsewhere it must quietly return null.
    [Fact]
    public void ReturnsAPercentOrNull()
    {
        var value = NvidiaGpuUsage.GetUtilizationPercent();
        if (value is double percent) Assert.InRange(percent, 0, 100);
        Assert.Equal(value.HasValue, NvidiaGpuUsage.GetUtilizationPercent().HasValue);
    }
}
