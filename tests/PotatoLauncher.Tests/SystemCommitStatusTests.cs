namespace PotatoLauncher.Tests;

public class SystemCommitStatusTests
{
    [Theory]
    [InlineData(94, 100, false)]
    [InlineData(95, 100, true)]
    [InlineData(100, 100, true)]
    [InlineData(0, 0, false)]
    public void CriticalWarningTracksCommitNotWorkingSet(ulong used, ulong limit, bool critical)
    {
        var status = new SystemCommitStatus(used * 1073741824, limit * 1073741824);
        Assert.Equal(critical, status.IsCritical);
        Assert.Equal(critical, status.Summary.Contains("WARNING"));
        if (critical) Assert.Contains("Trimming does not free commit", status.Summary);
    }

    [Fact]
    public void WindowsReturnsCommitCapacity()
    {
        var status = SystemCommitStatus.Read();
        Assert.True(status.LimitBytes > 0);
        Assert.InRange(status.UsedBytes, 1UL, status.LimitBytes);
    }
}
