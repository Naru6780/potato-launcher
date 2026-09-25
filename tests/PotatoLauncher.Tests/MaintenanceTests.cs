using System.Drawing;
using System.IO;
using System.Reflection;

namespace PotatoLauncher.Tests;

public class MaintenanceTests
{
    [Fact]
    public void SupersededProfileRetainsThresholdAndPreferences()
    {
        var settings = OptimizerSettings.DeserializeCompatible("""
            { "cpuAssignmentMode":"BalancedShared", "memoryTrimMode":"BandBudget",
              "trimTriggerMBPerClient":2024, "workingSetTrimEnabled":false, "mainLogicalProcessors":4 }
            """);
        Assert.Equal(CpuAssignmentMode.AllAvailableCores, settings.CpuAssignmentMode);
        Assert.Equal(MemoryTrimMode.Threshold, settings.MemoryTrimMode);
        Assert.Equal(2024, settings.TrimTriggerMBPerClient);
        Assert.False(settings.WorkingSetTrimEnabled);
        Assert.Equal(4, settings.MainLogicalProcessors);
    }

    [Fact]
    public void V106ProfileModesAreUnchanged()
    {
        var settings = OptimizerSettings.DeserializeCompatible("""
            { "cpuAssignmentMode":"AdaptiveSharedPools", "memoryTrimMode":"PressureAware", "trimTriggerMBPerClient":500 }
            """);
        Assert.Equal(CpuAssignmentMode.AdaptiveSharedPools, settings.CpuAssignmentMode);
        Assert.Equal(MemoryTrimMode.PressureAware, settings.MemoryTrimMode);
        Assert.Equal(500, settings.TrimTriggerMBPerClient);
    }

    [Fact]
    public void AtomicSettingsWriteReplacesExistingFileWithoutTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PotatoAtomicTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "settings.json");
            AtomicTextFile.Write(path, "old");
            AtomicTextFile.Write(path, "new");
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void NewsStopsOnResourceFailureAndDoesNotRepaintIdleSlides()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var control = new NewsBandrollControl { Size = new Size(300, 60) };
                control.SetSlides([new(new Bitmap(40, 40), "", "one"), new(new Bitmap(40, 40), "", "two")]);
                _ = control.Handle;
                var repaints = 0;
                control.Invalidated += (_, _) => repaints++;
                var tick = typeof(NewsBandrollControl).GetMethod("UpdateRollAnimation", BindingFlags.NonPublic | BindingFlags.Instance)!;
                for (var i = 0; i < 10; i++) tick.Invoke(control, null);
                Assert.Equal(0, repaints);
                control.RenderSafely(() => throw new OutOfMemoryException());
                Assert.False(control.Visible);
                Assert.False(control.HasSlides);
                control.RenderSafely(() => throw new Exception("Must not retry"));
                control.SetSlides([new(new Bitmap(10, 10), "", "ignored")]);
                Assert.False(control.HasSlides);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
