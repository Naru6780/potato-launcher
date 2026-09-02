using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PotatoLauncher.Tests;

public class OptimizerMonitorFormTests
{
    [Theory]
    [InlineData((int)CpuAssignmentMode.SplitLanes, true, true, true)]
    [InlineData((int)CpuAssignmentMode.AdaptiveSharedPools, true, false, true)]
    [InlineData((int)CpuAssignmentMode.OnePhysicalCorePerClient, false, false, false)]
    [InlineData((int)CpuAssignmentMode.AllAvailableCores, false, false, false)]
    public void ProcessorCountControls_MatchAssignmentMode(
        int modeValue,
        bool mainEnabled,
        bool followerEnabled,
        bool reservedEnabled)
    {
        var mode = (CpuAssignmentMode)modeValue;
        Assert.Equal(mainEnabled, OptimizerMonitorForm.UsesManualMainProcessorCount(mode));
        Assert.Equal(followerEnabled, OptimizerMonitorForm.UsesManualFollowerProcessorCount(mode));
        Assert.Equal(reservedEnabled, OptimizerMonitorForm.UsesReservedProcessorCount(mode));
    }

    [Fact]
    public void RefreshView_IgnoresGridAfterColumnsAreDisposed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var optimizer = new IntegratedOptimizerService(new OptimizerSettings());
                var palette = new ThemePalette(
                    Color.Black,
                    Color.Black,
                    Color.Black,
                    Color.Gray,
                    Color.White,
                    Color.LightGray,
                    Color.Blue,
                    Color.DarkBlue,
                    Color.Red,
                    Color.Black);
                using var form = new OptimizerMonitorForm(optimizer, palette);
                var gridField = typeof(OptimizerMonitorForm).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var refreshMethod = typeof(OptimizerMonitorForm).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var grid = (DataGridView)gridField.GetValue(form)!;

                grid.Columns.Clear();
                refreshMethod.Invoke(form, null);
            }
            catch (TargetInvocationException exception)
            {
                failure = exception.InnerException ?? exception;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The WinForms regression test timed out.");
        Assert.Null(failure);
    }
}
