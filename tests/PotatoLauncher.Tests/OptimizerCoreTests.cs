using System.Drawing;

namespace PotatoLauncher.Tests;

public class OptimizerCoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryEmptyWorkingSet_InvalidProcessId_ReturnsFalse(int processId)
    {
        Assert.False(NativeMethods.TryEmptyWorkingSet(processId));
    }

    [Fact]
    public void OptimizerSettings_DefaultsToPressureAwareMemoryTrimming()
    {
        Assert.Equal(MemoryTrimMode.PressureAware, new OptimizerSettings().MemoryTrimMode);
    }

    [Fact]
    public void OptimizerSettings_Normalize_RemovesInvalidValues()
    {
        var settings = new OptimizerSettings
        {
            TrimTriggerMBPerClient = 10,
            TrimIntervalSeconds = 0,
            TrimCooldownSeconds = 0,
            TargetFps = 1000,
            ManualMainClientIds = [7, 7, -1]
        };

        settings.Normalize();

        Assert.Equal(128, settings.TrimTriggerMBPerClient);
        Assert.Equal(1, settings.TrimIntervalSeconds);
        Assert.Equal(1, settings.TrimCooldownSeconds);
        Assert.Equal(360, settings.TargetFps);
        Assert.Equal([7], settings.ManualMainClientIds);
    }

    [Fact]
    public void OptimizerSettings_IgnoresRemovedCpuLaneKeys()
    {
        // Profiles written before 1.0.119 still carry the CPU-lane settings; they must load and simply be dropped.
        var settings = OptimizerSettings.DeserializeCompatible("""
            { "optimizerEnabled": true, "cpuAffinityOptimizationEnabled": true, "cpuPreviewOnly": false,
              "cpuAssignmentMode": "SplitLanes", "mainLogicalProcessors": 6, "followerLogicalProcessors": 4,
              "systemReservedLogicalProcessors": 4, "cpuLaneIntervalSeconds": 5,
              "mainReservedLogicalProcessorsByName": { "Artemis Potato": 6 },
              "targetFps": 60, "workingSetTrimEnabled": true }
            """);
        settings.Normalize();

        Assert.Equal(60, settings.TargetFps);
        Assert.True(settings.WorkingSetTrimEnabled);
        Assert.DoesNotContain("cpuAssignmentMode", System.Text.Json.JsonSerializer.Serialize(settings), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("FINAL FANTASY XIV - Artemis Potato", "Artemis Potato")]
    [InlineData("FFXIV: Hermes Potato", "Hermes Potato")]
    [InlineData("", "Untitled FFXIV client")]
    public void ExtractCharacterName_RemovesCommonWindowTitlePrefixes(string title, string expected)
    {
        Assert.Equal(expected, IntegratedOptimizerService.ExtractCharacterName(title));
    }

    [Fact]
    public void NativeGridColor_ForcesOpaqueColorForWinFormsGridProperties()
    {
        var color = OptimizerMonitorForm.NativeGridColor(Color.FromArgb(42, 10, 20, 30));

        Assert.Equal(255, color.A);
        Assert.Equal(10, color.R);
        Assert.Equal(20, color.G);
        Assert.Equal(30, color.B);
    }

    [Fact]
    public void SystemUsageSampler_ReturnsMemoryTotals()
    {
        using var sampler = new SystemUsageSampler();

        var snapshot = sampler.GetSnapshot(gpuPercent: 12.5);

        Assert.True(snapshot.TotalMemoryBytes >= 0);
        Assert.InRange(snapshot.UsedMemoryBytes, 0, Math.Max(0, snapshot.TotalMemoryBytes));
        Assert.Equal(12.5, snapshot.GpuPercent);
    }

    [Fact]
    public void OptimizerSettings_MainCandidates_ArePersistentAndOrderedByLocalPriority()
    {
        var settings = new OptimizerSettings();
        settings.SetMainCandidate("Kazuko Aura", true);
        settings.SetMainCandidate("Garrison Mangler", true);
        settings.SetMainCandidate("Wind-up Garrison", true);

        settings.SetMainPriority("Garrison Mangler", 1);
        settings.SetMainPriority("Wind-up Garrison", 2);
        settings.SetMainPriority("Kazuko Aura", 3);

        Assert.Equal(
            ["Garrison Mangler", "Wind-up Garrison", "Kazuko Aura"],
            settings.MainClientRules.Select(rule => rule.ClientName));
        Assert.Equal([1, 2, 3], settings.MainClientRules.Select(rule => rule.Priority));
        Assert.True(settings.IsMainCandidate("  wind-up   garrison "));
        Assert.False(settings.IsMainCandidate("Someone Else"));
    }

    [Fact]
    public void MainClientSelector_UsesLocalPriorityAndTreatsOtherCandidatesAsStandby()
    {
        var settings = new OptimizerSettings
        {
            MainClientRules =
            [
                new MainClientRule { ClientName = "Primary", Priority = 1 },
                new MainClientRule { ClientName = "Backup", Priority = 2 }
            ]
        };
        settings.Normalize();
        var clients = new[]
        {
            new MainClientIdentity(10, "Follower", new DateTime(2026, 1, 1)),
            new MainClientIdentity(20, "Backup", new DateTime(2026, 1, 2)),
            new MainClientIdentity(30, "Primary", new DateTime(2026, 1, 3))
        };

        var selection = MainClientSelector.Select(clients, settings);

        Assert.Equal([30], selection.ActiveMainClientIds);
        Assert.True(selection.CandidateClientIds.SetEquals([20, 30]));
    }

    [Fact]
    public void MemoryPressurePolicy_UsesStartThresholdAndHysteresis()
    {
        var settings = new OptimizerSettings
        {
            MemoryPressureStartPercent = 85,
            MemoryPressureStopPercent = 75,
            CriticalAvailableMemoryMB = 4096
        };

        Assert.False(MemoryPressurePolicy.Evaluate(false, 70, 20_000, settings));
        Assert.True(MemoryPressurePolicy.Evaluate(false, 86, 8_000, settings));
        Assert.True(MemoryPressurePolicy.Evaluate(false, 70, 4_000, settings));
        Assert.True(MemoryPressurePolicy.Evaluate(true, 76, 20_000, settings));
        Assert.True(MemoryPressurePolicy.Evaluate(true, 70, 5_000, settings));
        Assert.False(MemoryPressurePolicy.Evaluate(true, 70, 8_000, settings));
    }
}
