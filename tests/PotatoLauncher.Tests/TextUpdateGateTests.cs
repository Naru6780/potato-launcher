namespace PotatoLauncher.Tests;

public class TextUpdateGateTests
{
    [Fact]
    public void ShouldApply_AllowsFirstAndChangedTextImmediately()
    {
        var gate = new TextUpdateGate(TimeSpan.FromSeconds(1));
        var now = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(gate.ShouldApply("Waiting for client...", now));
        Assert.True(gate.ShouldApply("Client is ready.", now.AddMilliseconds(100)));
    }

    [Fact]
    public void ShouldApply_SkipsDuplicateTextUntilIntervalPasses()
    {
        var gate = new TextUpdateGate(TimeSpan.FromSeconds(1));
        var now = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(gate.ShouldApply("Waiting for client...", now));
        Assert.False(gate.ShouldApply("Waiting for client...", now.AddMilliseconds(250)));
        Assert.True(gate.ShouldApply("Waiting for client...", now.AddSeconds(2)));
    }

    [Fact]
    public void ShouldApply_ForceBypassesDuplicateThrottle()
    {
        var gate = new TextUpdateGate(TimeSpan.FromSeconds(1));
        var now = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(gate.ShouldApply("Ready.", now));
        Assert.True(gate.ShouldApply("Ready.", now.AddMilliseconds(50), force: true));
    }
}
