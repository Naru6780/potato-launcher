namespace PotatoLauncher.Tests;

public class PerClientTrimPolicyTests
{
    [Theory]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(2024, true)]
    public void ThresholdIsPerClient(double resident, bool expected)
        => Assert.Equal(expected, PerClientTrimPolicy.IsEligible(resident, 500, null, DateTime.UtcNow, 30, false));

    [Fact]
    public void CooldownAndManualTrimBehaveAsExpected()
    {
        var now = DateTime.UtcNow;
        Assert.False(PerClientTrimPolicy.IsEligible(2500, 2024, now.AddSeconds(-29), now, 30, false));
        Assert.True(PerClientTrimPolicy.IsEligible(2500, 2024, now.AddSeconds(-30), now, 30, false));
        Assert.True(PerClientTrimPolicy.IsEligible(1000, 2024, now, now, 30, true));
        Assert.False(PerClientTrimPolicy.IsEligible(double.NaN, 2024, null, now, 30, true));
    }

    [Fact]
    public void BudgetPreviewMigratesBackWithoutLosingSavedThresholdOrEnabledChoice()
    {
        var settings = new OptimizerSettings { MemoryTrimMode = MemoryTrimMode.BandBudget,
            TrimTriggerMBPerClient = 2024, WorkingSetTrimEnabled = true };
        settings.Normalize();
        Assert.Equal(MemoryTrimMode.Threshold, settings.MemoryTrimMode);
        Assert.Equal(2024, settings.TrimTriggerMBPerClient);
        Assert.True(settings.WorkingSetTrimEnabled);
    }
}
