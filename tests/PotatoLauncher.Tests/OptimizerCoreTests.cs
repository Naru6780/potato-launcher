using System.Drawing;

namespace PotatoLauncher.Tests;

public class OptimizerCoreTests
{
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
}
