using System.Drawing;

namespace PotatoLauncher.Tests;

public class OptimizerCoreTests
{
    [Fact]
    public void OptimizerSettings_DefaultsToLiveCpuOptimization()
    {
        Assert.False(new OptimizerSettings().CpuPreviewOnly);
    }

    [Fact]
    public void OptimizerSettings_DefaultsToPressureAwareMemoryTrimming()
    {
        Assert.Equal(MemoryTrimMode.PressureAware, new OptimizerSettings().MemoryTrimMode);
    }

    [Fact]
    public void ProcessorAffinity_CreateMask_ClampsToLogicalProcessorCount()
    {
        var mask = ProcessorAffinity.CreateMask(1, 8, 4);

        Assert.Equal((1L << 1) | (1L << 2) | (1L << 3), mask);
        Assert.Equal(3, ProcessorAffinity.CountSetBits(mask));
    }

    [Fact]
    public void OptimizerSettings_Normalize_RemovesInvalidValues()
    {
        var settings = new OptimizerSettings
        {
            MainLogicalProcessors = 999,
            FollowerLogicalProcessors = -10,
            SystemReservedLogicalProcessors = 999,
            TrimTriggerMBPerClient = 10,
            TrimIntervalSeconds = 0,
            TrimCooldownSeconds = 0,
            CpuLaneIntervalSeconds = 0,
            ManualMainClientIds = [7, 7, -1]
        };
        settings.MainReservedLogicalProcessorsByName["  Artemis   Potato  "] = 999;
        settings.MainReservedLogicalProcessorsByName["Hermes Potato"] = 0;
        settings.MainReservedLogicalProcessorsByName[""] = 4;

        settings.Normalize();

        Assert.Contains(settings.MainLogicalProcessors, OptimizerSettings.AllowedMainLogicalProcessors);
        Assert.Contains(settings.FollowerLogicalProcessors, OptimizerSettings.AllowedFollowerLogicalProcessors);
        Assert.InRange(settings.SystemReservedLogicalProcessors, 0, Math.Max(0, Environment.ProcessorCount - 1));
        Assert.Equal(128, settings.TrimTriggerMBPerClient);
        Assert.Equal(1, settings.TrimIntervalSeconds);
        Assert.Equal(1, settings.TrimCooldownSeconds);
        Assert.Equal(1, settings.CpuLaneIntervalSeconds);
        Assert.Equal([7], settings.ManualMainClientIds);
        Assert.InRange(settings.GetMainReservedLogicalProcessors("Artemis Potato"), 1, Math.Max(1, Environment.ProcessorCount));
        Assert.Equal(0, settings.GetMainReservedLogicalProcessors("Hermes Potato"));
    }

    [Fact]
    public void OptimizerSettings_ProcessorChoices_UseDetectedLogicalProcessorCount()
    {
        var choices = OptimizerSettings.GetAllowedLogicalProcessorCounts(32);

        Assert.Equal(Enumerable.Range(1, 32), choices);
    }

    [Fact]
    public void OptimizerSettings_ProcessorChoices_RespectNativeAffinityMaskCapacity()
    {
        var choices = OptimizerSettings.GetAllowedLogicalProcessorCounts(128);

        Assert.Equal(ProcessorAffinity.MaskBitCount, choices.Count);
        Assert.Equal(ProcessorAffinity.MaskBitCount, choices[^1]);
    }

    [Theory]
    [InlineData(32, "32 logical CPUs")]
    [InlineData(128, "128 logical CPUs detected (64 affinity-addressable)")]
    public void ProcessorAffinity_FormatLogicalProcessorCapacity_ReportsDetectedAndSupportedCounts(int detected, string expected)
    {
        Assert.Equal(expected, ProcessorAffinity.FormatLogicalProcessorCapacity(detected));
    }

    [Fact]
    public void OptimizerSettings_Normalize_UsesDetectedLogicalProcessorCount()
    {
        var settings = new OptimizerSettings
        {
            MainLogicalProcessors = 32,
            FollowerLogicalProcessors = 24,
            SystemReservedLogicalProcessors = 40
        };
        settings.MainReservedLogicalProcessorsByName["Artemis Potato"] = 40;

        settings.Normalize(32);

        Assert.Equal(32, settings.MainLogicalProcessors);
        Assert.Equal(24, settings.FollowerLogicalProcessors);
        Assert.Equal(31, settings.SystemReservedLogicalProcessors);
        Assert.Equal(32, settings.GetMainReservedLogicalProcessors("Artemis Potato"));
    }

    [Fact]
    public void ProcessorAffinity_UsesAllBitsInNativeAffinityMask()
    {
        var mask = ProcessorAffinity.CreateMask(0, 64, 64);

        Assert.Equal(64, ProcessorAffinity.CountSetBits(mask));
    }

    [Fact]
    public void OptimizerSettings_MainReservation_ZeroClearsOverride()
    {
        var settings = new OptimizerSettings();

        settings.SetMainReservedLogicalProcessors("Artemis Potato", 6);
        settings.SetMainReservedLogicalProcessors("Artemis Potato", 0);

        Assert.Equal(0, settings.GetMainReservedLogicalProcessors("Artemis Potato"));
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

    [Fact]
    public void AdaptiveSharedPools_ProtectsMainAndSystemCores_On9950X3DTopology()
    {
        var clients = Enumerable.Range(1, 16).ToArray();
        var physicalCores = Enumerable.Range(0, 16)
            .Select(index => (1L << (index * 2)) | (1L << (index * 2 + 1)))
            .ToArray();
        var cacheDomains = new[]
        {
            new ProcessorCacheDomain(0x0000FFFF, 96L * 1024 * 1024, 3),
            new ProcessorCacheDomain(unchecked((long)0xFFFF0000), 32L * 1024 * 1024, 3)
        };

        var masks = CpuAffinityAllocator.CreateAdaptiveMasks(
            clients,
            new HashSet<int> { 1 },
            new Dictionary<int, int> { [1] = 12 },
            physicalCores,
            cacheDomains,
            systemReservedLogicalProcessors: 2);

        const long mainMask = 0x00000FFF;
        const long remainingCachePool = 0x0000F000;
        const long frequencyPool = 0x3FFF0000;
        const long systemMask = unchecked((long)0xC0000000);
        Assert.Equal(mainMask, masks[1]);
        Assert.All(clients.Skip(1), id => Assert.Contains(masks[id], new[] { remainingCachePool, frequencyPool }));
        Assert.All(clients.Skip(1), id => Assert.Equal(0, masks[id] & mainMask));
        Assert.All(clients, id => Assert.Equal(0, masks[id] & systemMask));
        Assert.Equal(4, clients.Skip(1).Count(id => masks[id] == remainingCachePool));
        Assert.Equal(11, clients.Skip(1).Count(id => masks[id] == frequencyPool));
    }

    [Fact]
    public void AdaptiveSharedPools_PrefersLargestCacheDomainForMain()
    {
        var physicalCores = Enumerable.Range(0, 8)
            .Select(index => (1L << (index * 2)) | (1L << (index * 2 + 1)))
            .ToArray();
        var domains = new[]
        {
            new ProcessorCacheDomain(0x00FF, 16L * 1024 * 1024, 3),
            new ProcessorCacheDomain(0xFF00, 64L * 1024 * 1024, 3)
        };

        var masks = CpuAffinityAllocator.CreateAdaptiveMasks(
            [10, 20],
            new HashSet<int> { 10 },
            new Dictionary<int, int> { [10] = 4 },
            physicalCores,
            domains,
            systemReservedLogicalProcessors: 0);

        Assert.Equal(0x0F00, masks[10]);
        Assert.Equal(0, masks[10] & masks[20]);
    }
}
