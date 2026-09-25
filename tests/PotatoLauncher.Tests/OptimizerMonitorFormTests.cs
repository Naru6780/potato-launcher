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
    [InlineData((int)CpuAssignmentMode.BalancedShared, false, false, false)]
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
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.Show();
                var assignment = (ComboBox)typeof(OptimizerMonitorForm).GetField("assignmentMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Assert.Equal(CpuAssignmentMode.BalancedShared, assignment.SelectedItem);
                var trimMode = (ComboBox)typeof(OptimizerMonitorForm).GetField("trimMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                var trimTrigger = (NumericUpDown)typeof(OptimizerMonitorForm).GetField("trimTrigger", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Assert.Equal("Auto trim at threshold", trimMode.SelectedItem);
                Assert.Equal(1024, trimTrigger.Value);
                Assert.Equal(128, trimTrigger.Minimum);
                Assert.Equal(32768, trimTrigger.Maximum);
                var mainLabel = (Label)typeof(OptimizerMonitorForm).GetField("mainClientsLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                var updateMainLabel = typeof(OptimizerMonitorForm).GetMethod("UpdateMainClientsLabel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var stableSnapshots = optimizer.GetSnapshots();
                updateMainLabel.Invoke(form, [stableSnapshots]);
                var labelChanges = 0;
                mainLabel.TextChanged += (_, _) => labelChanges++;
                for (var i = 0; i < 10; i++) updateMainLabel.Invoke(form, [stableSnapshots]);
                Assert.Equal(0, labelChanges);
                var refreshStable = typeof(OptimizerMonitorForm).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic)!;
                refreshStable.Invoke(form, null);
                refreshStable.Invoke(form, null);
                Assert.Equal(0, labelChanges);
                Assert.Equal(CpuAssignmentMode.BalancedShared, optimizer.Settings.CpuAssignmentMode);
                // Normalization clamps the default to this machine's available CPU count.
                Assert.Equal(Math.Min(6, Environment.ProcessorCount), optimizer.Settings.MainLogicalProcessors);
                foreach (var size in new[] { new Size(1120, 780), new Size(940, 680) })
                {
                    form.Size = size;
                    form.PerformLayout();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    foreach (var name in new[] { "presetButton", "fpsButton", "restoreButton", "cpuOperationMode" })
                    {
                        var control = (Control)typeof(OptimizerMonitorForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        Assert.True(control.Parent!.ClientRectangle.Contains(control.Bounds), $"{name} clipped at {size}");
                    }
                    var renderDirectory = Environment.GetEnvironmentVariable("POTATO_OPTIMIZER_TEST_RENDERS");
                    if (!string.IsNullOrWhiteSpace(renderDirectory))
                    {
                        System.IO.Directory.CreateDirectory(renderDirectory);
                        bitmap.Save(System.IO.Path.Combine(renderDirectory, $"optimizer-{size.Width}.png"));
                    }
                }
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
