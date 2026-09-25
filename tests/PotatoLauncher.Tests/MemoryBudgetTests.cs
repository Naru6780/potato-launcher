namespace PotatoLauncher.Tests;

public class MemoryBudgetTests
{
    private readonly DateTime now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    private MemoryClientSample Sample(int id = 1, double ram = 5000) => new(new(id, now.AddMinutes(-5)), ram, 6000);

    [Theory]
    [InlineData(29, 20, false)]
    [InlineData(30, 20, false)]
    [InlineData(31, 20, true)]
    [InlineData(20, 1, true)]
    public void BudgetTriggersOnTotalRamOrSystemPressure(double gamesGiB, double availableGiB, bool expected)
    {
        Assert.Equal(expected, MemoryBudget.ShouldTrim(gamesGiB * 1024, 30 * 1024, 64 * 1024, availableGiB * 1024));
    }

    [Fact]
    public void OnlyOneTrimCanBeObservedAtATime()
    {
        var feedback = new MemoryTrimFeedback();
        var client = Sample();
        Assert.True(feedback.CanTrim(client.Key, now));
        feedback.Record(client, 500, 2000, now);
        Assert.False(feedback.CanTrim(Sample(2).Key, now));
        feedback.Observe([client with { ResidentMb = 600 }], 6000, now.AddSeconds(9));
        Assert.True(feedback.IsObserving);
    }

    [Fact]
    public void SuccessfulTrimRetainsSavingsEvenWhenAnotherClientConsumesAvailableRam()
    {
        var feedback = new MemoryTrimFeedback();
        var client = Sample();
        feedback.Record(client, 500, 4000, now);
        feedback.Observe([client with { ResidentMb = 700 }], 2000, now.AddSeconds(10));
        Assert.False(feedback.IsObserving);
        Assert.Contains("savings persisted", feedback.Status);
        Assert.False(feedback.CanTrim(client.Key, now.AddSeconds(13)));
        Assert.True(feedback.CanTrim(Sample(2).Key, now.AddSeconds(13)));
        Assert.True(feedback.CanTrim(client.Key, now.AddMinutes(3)));
    }

    [Fact]
    public void ReboundPausesOtherTrimsAndLongerForAffectedClient()
    {
        var feedback = new MemoryTrimFeedback();
        var client = Sample();
        feedback.Record(client, 500, 2000, now);
        feedback.Observe([client with { ResidentMb = 4900 }], 2100, now.AddSeconds(10));
        Assert.Contains("rebounded", feedback.Status);
        Assert.False(feedback.CanTrim(Sample(2).Key, now.AddMinutes(1)));
        Assert.True(feedback.CanTrim(Sample(2).Key, now.AddMinutes(3)));
        Assert.False(feedback.CanTrim(client.Key, now.AddMinutes(3)));
        Assert.True(feedback.CanTrim(client.Key, now.AddMinutes(11)));
    }

    [Fact]
    public void MissingClientOrReusedPidCannotBeReportedAsSuccessfulReclaim()
    {
        var feedback = new MemoryTrimFeedback();
        var client = Sample();
        feedback.Record(client, 500, 2000, now);
        feedback.Observe([client with { Key = new(1, now) }], 8000, now.AddSeconds(10));
        Assert.False(feedback.IsObserving);
        Assert.Contains("could not be sampled", feedback.Status);
        Assert.False(feedback.CanTrim(new(1, now), now.AddSeconds(20)));
    }

    [Fact]
    public void FailedTrimsBackOffAndNewClientsAreProtected()
    {
        var feedback = new MemoryTrimFeedback();
        var client = Sample();
        feedback.Failed(client.Key, now);
        Assert.False(feedback.CanTrim(client.Key, now.AddMinutes(2)));
        Assert.True(feedback.CanTrim(client.Key, now.AddMinutes(6)));
        Assert.False(feedback.CanTrim(new(3, now), now.AddSeconds(20)));
    }

    [Theory]
    [InlineData((int)MemoryTrimMode.Threshold)]
    [InlineData((int)MemoryTrimMode.PressureAware)]
    public void ExistingPerClientModeIsPreservedWithoutEnablingTrims(int legacy)
    {
        var settings = new OptimizerSettings { MemoryTrimMode = (MemoryTrimMode)legacy };
        settings.Normalize();
        Assert.Equal((MemoryTrimMode)legacy, settings.MemoryTrimMode);
        Assert.Equal(30720, settings.BandMemoryBudgetMB);
        Assert.False(settings.WorkingSetTrimEnabled);
    }
}
