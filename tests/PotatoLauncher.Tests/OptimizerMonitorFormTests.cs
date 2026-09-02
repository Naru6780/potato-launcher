using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PotatoLauncher.Tests;

public class OptimizerMonitorFormTests
{
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
