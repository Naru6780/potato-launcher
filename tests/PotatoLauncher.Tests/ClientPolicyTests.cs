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
        settings.Normalize(16);
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

public class MinimizedClientPolicyTests
{
    [Fact]
    public void MinimizedClientsGetOnlyTheirTimerCoarsened()
    {
        var settings = new OptimizerSettings();
        Assert.True(ClientPolicy.Desired(ClientRole.Background, settings, minimized: true).TimerThrottled);
        Assert.True(ClientPolicy.Desired(ClientRole.Active, settings, minimized: true).TimerThrottled);
        Assert.False(ClientPolicy.Desired(ClientRole.Background, settings, minimized: false).TimerThrottled);

        settings.LimitMinimizedClients = false;
        Assert.False(ClientPolicy.Desired(ClientRole.Background, settings, minimized: true).TimerThrottled);
        settings.LimitMinimizedClients = true;
        settings.PreventWindowsThrottling = false;
        Assert.False(ClientPolicy.Desired(ClientRole.Background, settings, minimized: true).TimerThrottled);
    }

    [Fact]
    public void UncappedClientsAreReported()
    {
        var clients = new List<OptimizerClientSnapshot>
        {
            new(1, "A@W", "", false, false, 8, null, 0, 0, 0, 0, null, null, false, null, 244),
            new(2, "B@W", "", false, false, 3, null, 0, 0, 0, 0, null, null, false, null, 60)
        };
        Assert.Contains(OptimizerDiagnostics.Get(clients, 60), finding => finding.StartsWith("1 client is running uncapped (244 FPS)"));
    }
}
