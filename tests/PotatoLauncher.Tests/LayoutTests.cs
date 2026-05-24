namespace PotatoLauncher.Tests;

public class LayoutTests
{
    [Fact]
    public void AccountPanelMaximum_UsesAvailableWideScreenSpace()
    {
        var metrics = LauncherLayoutMetrics.Calculate(clientWidth: 2048, clientHeight: 1060, requestedAccountWidth: 1200);

        Assert.True(metrics.AccountWidth >= 1000);
        Assert.True(metrics.BandWidth >= 420);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(0)]
    [InlineData(5000)]
    public void LauncherLayoutMetrics_ClampPanelsToUsableWidths(int requestedWidth)
    {
        var metrics = LauncherLayoutMetrics.Calculate(clientWidth: 990, clientHeight: 700, requestedAccountWidth: requestedWidth);

        Assert.True(metrics.AccountWidth >= 300);
        Assert.True(metrics.BandWidth >= 420);
        Assert.Equal(20, metrics.Gap);
    }
}
