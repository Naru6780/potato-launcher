namespace PotatoLauncher.Tests;

public class CapacityPlannerTests
{
    private const long GB = 1024L * 1024 * 1024;

    private static CapacityClientSample Client(double cpu, double wsGb = 3, double privGb = 8, double? fps = 60) =>
        new(cpu, (long)(wsGb * GB), (long)(privGb * GB), fps);

    [Fact]
    public void CpuIsTheLimitWhenItRunsOutFirst()
    {
        var clients = Enumerable.Range(0, 8).Select(_ => Client(6)).ToList();
        var estimate = CapacityPlanner.Estimate(clients, systemCpuPercent: 60, availablePhysicalBytes: 40 * GB, commitAvailableBytes: 100 * GB, targetFps: 60);
        Assert.Equal(5, estimate.AdditionalClients); // (90 - 60) / 6
        Assert.Equal("CPU", estimate.LimitingFactor);
    }

    [Fact]
    public void RamIsTheLimitWhenItRunsOutFirst()
    {
        var clients = Enumerable.Range(0, 8).Select(_ => Client(2, wsGb: 3)).ToList();
        var estimate = CapacityPlanner.Estimate(clients, 30, availablePhysicalBytes: 10 * GB, commitAvailableBytes: 100 * GB, 60);
        Assert.Equal(2, estimate.AdditionalClients); // (10 - 4) / 3
        Assert.Equal("RAM", estimate.LimitingFactor);
    }

    [Fact]
    public void NoRoomWhenAClientIsAlreadyBelowItsCap()
    {
        var clients = new List<CapacityClientSample> { Client(6), Client(6, fps: 48) };
        var estimate = CapacityPlanner.Estimate(clients, 95, 40 * GB, 100 * GB, 60);
        Assert.Equal(0, estimate.AdditionalClients);
        Assert.Contains("1 client is below 60 FPS", estimate.Summary);
    }

    [Fact]
    public void NoClientsMeansNoEstimate()
    {
        Assert.Null(CapacityPlanner.Estimate([], 10, 40 * GB, 100 * GB, 60).AdditionalClients);
        Assert.Equal(2.5, CapacityPlanner.Median([1, 2, 3, 4]));
    }
}
