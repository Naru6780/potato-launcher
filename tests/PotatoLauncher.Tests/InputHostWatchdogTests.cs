namespace PotatoLauncher.Tests;

public class InputHostWatchdogTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResetsOnlyAfterASustainedSpinAndThenWaits()
    {
        var watchdog = new InputHostWatchdog();
        Assert.False(watchdog.ShouldReset(5, T0));
        Assert.False(watchdog.ShouldReset(5, T0.AddSeconds(20)));
        Assert.False(watchdog.ShouldReset(0.1, T0.AddSeconds(25))); // dropped: spin streak resets
        Assert.False(watchdog.ShouldReset(5, T0.AddSeconds(30)));
        Assert.True(watchdog.ShouldReset(5, T0.AddSeconds(60)));
    }

    [Fact]
    public void IdleHostIsNeverTouched()
    {
        var watchdog = new InputHostWatchdog();
        for (var seconds = 0; seconds < 600; seconds += 10) Assert.False(watchdog.ShouldReset(1.9, T0.AddSeconds(seconds)));
    }
}
