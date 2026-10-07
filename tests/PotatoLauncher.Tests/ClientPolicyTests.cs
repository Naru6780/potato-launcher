using System.Diagnostics;

namespace PotatoLauncher.Tests;

public class ClientPolicyTests
{
    [Fact]
    public void ForegroundAndMainClientsAreActive()
    {
        var clients = new[] { 10, 20, 30, 40 };
        var active = ClientPolicy.ActiveClientIds(clients, mainCandidateIds: [20, 99], foregroundProcessId: 30, followForeground: true);
        Assert.Equal(new HashSet<int> { 20, 30 }, active);
    }

    [Fact]
    public void ForegroundIsIgnoredWhenNotAClientOrWhenDisabled()
    {
        var clients = new[] { 10, 20 };
        Assert.Empty(ClientPolicy.ActiveClientIds(clients, [], foregroundProcessId: 555, followForeground: true));
        Assert.Empty(ClientPolicy.ActiveClientIds(clients, [], foregroundProcessId: 10, followForeground: false));
    }

    [Fact]
    public void DefaultsNeverLowerAnyClientAndKeepThrottlingOff()
    {
        var settings = new OptimizerSettings();
        var active = ClientPolicy.Desired(ClientRole.Active, settings);
        var background = ClientPolicy.Desired(ClientRole.Background, settings);

        Assert.Equal(ProcessPriorityClass.AboveNormal, active.Priority);
        Assert.Equal(ProcessPriorityClass.Normal, background.Priority);
        Assert.True(active.PreventThrottling);
        Assert.True(background.PreventThrottling);
        Assert.False(active.LowMemoryPriority);
        Assert.True(background.LowMemoryPriority);
    }

    [Fact]
    public void UnsafePrioritiesAreClampedOnLoad()
    {
        var settings = new OptimizerSettings
        {
            ActiveClientPriority = ProcessPriorityClass.RealTime,
            BackgroundClientPriority = ProcessPriorityClass.Idle
        };
        settings.Normalize();
        Assert.Equal(ProcessPriorityClass.AboveNormal, settings.ActiveClientPriority);
        Assert.Equal(ProcessPriorityClass.Normal, settings.BackgroundClientPriority);
    }

    [Fact]
    public void PrioritiesRoundTripThroughSettingsJson()
    {
        var settings = OptimizerSettings.DeserializeCompatible("""{ "activeClientPriority": "Normal", "backgroundClientPriority": "BelowNormal", "clientPolicyEnabled": false }""");
        Assert.Equal(ProcessPriorityClass.Normal, settings.ActiveClientPriority);
        Assert.Equal(ProcessPriorityClass.BelowNormal, settings.BackgroundClientPriority);
        Assert.False(settings.ClientPolicyEnabled);
        Assert.True(OptimizerSettings.DeserializeCompatible("{}").ClientPolicyEnabled);
    }
}

public class MinimizedClientLimiterTests
{
    [Fact]
    public void CapStartsModestAndSteersTowardTheTarget()
    {
        Assert.Equal(MinimizedClientLimiter.InitialCap, MinimizedClientLimiter.NextCap(0, 270, 60));
        Assert.True(MinimizedClientLimiter.NextCap(300, 87, 60) < 300);   // too fast: tighten
        Assert.True(MinimizedClientLimiter.NextCap(300, 40, 60) > 300);   // too slow: loosen
        Assert.Equal(300u, MinimizedClientLimiter.NextCap(300, 62, 60));  // within tolerance: hold
        Assert.Equal(255u, MinimizedClientLimiter.NextCap(300, 400, 60)); // one step is bounded (x0.85)
        Assert.Equal(MinimizedClientLimiter.MinimumCap, MinimizedClientLimiter.NextCap(55, 400, 60));
        Assert.Equal(MinimizedClientLimiter.MaximumCap, MinimizedClientLimiter.NextCap(2400, 10, 60));
    }

    [Fact]
    public void UncappedClientsAreReported()
    {
        var clients = new List<OptimizerClientSnapshot>
        {
            new(1, "A@W", "", false, false, 8, null, 0, 0, 0, 0, null, 244, "", 0),
            new(2, "B@W", "", false, false, 3, null, 0, 0, 0, 0, null, 60)
        };
        Assert.Contains(OptimizerDiagnostics.Get(clients, 60), finding => finding.StartsWith("1 client is running above the target with no in-game frame limit (A@W 244 FPS)"));
        var capped = new List<OptimizerClientSnapshot> { new(3, "C@W", "", false, false, 8, null, 0, 0, 0, 0, null, 244, "", 60) };
        Assert.Contains(OptimizerDiagnostics.Get(capped, 60), finding => finding.Contains("despite an in-game limit"));
        var driverPaced = new List<OptimizerClientSnapshot> { new(4, "D@W", "", false, false, 8, null, 0, 0, 0, 0, null, 60, "", 0) };
        Assert.DoesNotContain(OptimizerDiagnostics.Get(driverPaced, 60), finding => finding.Contains("frame limit"));
    }
}
