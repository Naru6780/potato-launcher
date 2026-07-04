using System.Diagnostics;
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
            FollowerPriorityClass = ProcessPriorityClass.RealTime,
            ManualMainClientIds = [7, 7, -1]
        };
        settings.ClientPriorityOverridesByName["  Test   Client  "] = ProcessPriorityClass.High;
        settings.ClientPriorityOverridesByName["Bad"] = ProcessPriorityClass.RealTime;

        settings.Normalize();

        Assert.Contains(settings.MainLogicalProcessors, OptimizerSettings.AllowedMainLogicalProcessors);
        Assert.Contains(settings.FollowerLogicalProcessors, OptimizerSettings.AllowedFollowerLogicalProcessors);
        Assert.InRange(settings.SystemReservedLogicalProcessors, 0, Math.Max(0, Environment.ProcessorCount - 1));
        Assert.Equal(128, settings.TrimTriggerMBPerClient);
        Assert.Equal(1, settings.TrimIntervalSeconds);
        Assert.Equal(1, settings.TrimCooldownSeconds);
        Assert.Equal(1, settings.CpuLaneIntervalSeconds);
        Assert.Equal(ProcessPriorityClass.Normal, settings.FollowerPriorityClass);
        Assert.Equal([7], settings.ManualMainClientIds);
        Assert.Equal(ProcessPriorityClass.High, settings.GetClientPriorityOverride("Test Client"));
        Assert.Null(settings.GetClientPriorityOverride("Bad"));
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
}
