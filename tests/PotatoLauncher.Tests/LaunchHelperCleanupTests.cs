namespace PotatoLauncher.Tests;

public class LaunchHelperCleanupTests
{
    [Fact]
    public void SelectLaunchProcesses_IncludesOnlyNewAllowedHelpers()
    {
        var before = new HashSet<int> { 10, 20 };
        var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        var current = new[]
        {
            new LaunchHelperProcess(10, "XIVLauncher", now),
            new LaunchHelperProcess(20, "DalamudCrashHandler", now),
            new LaunchHelperProcess(30, "XIVLauncher", now.AddSeconds(1)),
            new LaunchHelperProcess(40, "DalamudCrashHandler", now.AddSeconds(2)),
            new LaunchHelperProcess(50, "Dalamud.Injector", now.AddSeconds(3)),
            new LaunchHelperProcess(60, "ffxiv_dx11", now.AddSeconds(4))
        };
        var parents = new Dictionary<int, int>
        {
            [30] = 1,
            [40] = 70,
            [50] = 30,
            [60] = 70,
            [70] = 30
        };

        var selected = LaunchHelperCleanup.SelectLaunchProcesses(before, current, directlyStartedProcessId: 30, gameClientProcessId: 70, parentProcessIds: parents);

        Assert.Equal([30, 40, 50], selected.Select(process => process.ProcessId));
    }

    [Fact]
    public void SelectLaunchProcesses_DeduplicatesProcessIds()
    {
        var now = DateTime.UtcNow;
        var selected = LaunchHelperCleanup.SelectLaunchProcesses(
            new HashSet<int>(),
            [
                new LaunchHelperProcess(30, "XIVLauncher", now),
                new LaunchHelperProcess(30, "XIVLauncher", now)
            ],
            directlyStartedProcessId: 30,
            gameClientProcessId: 70,
            parentProcessIds: new Dictionary<int, int>());

        Assert.Single(selected);
    }

    [Fact]
    public void SelectLaunchProcesses_DoesNotTakeHelpersOwnedByAnotherConcurrentClient()
    {
        var now = DateTime.UtcNow;
        var current = new[]
        {
            new LaunchHelperProcess(40, "DalamudCrashHandler", now),
            new LaunchHelperProcess(50, "DalamudCrashHandler", now)
        };
        var parents = new Dictionary<int, int>
        {
            [40] = 70,
            [50] = 80
        };

        var selected = LaunchHelperCleanup.SelectLaunchProcesses(
            new HashSet<int>(),
            current,
            directlyStartedProcessId: 30,
            gameClientProcessId: 70,
            parentProcessIds: parents);

        Assert.Equal([40], selected.Select(process => process.ProcessId));
    }
}
