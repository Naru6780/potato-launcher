using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PotatoLauncher.Tests;

public class DesktopPetFailureTests
{
    [Fact]
    public void DrawingFailureHidesPetReleasesResourcesAndPreventsRetries()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var sheets = Enum.GetValues<ArtemisAnimationState>()
                    .ToDictionary(state => state, _ => new Bitmap(120, 120));
                using var form = (ArtemisDesktopPetForm)Activator.CreateInstance(
                    typeof(ArtemisDesktopPetForm), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [sheets, 100], null)!;
                form.Location = new Point(-32000, -32000);
                form.Show();
                var render = typeof(ArtemisDesktopPetForm).GetMethod("RenderCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var buffer = typeof(ArtemisDesktopPetForm).GetField("frameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var pixels = typeof(ArtemisDesktopPetForm).GetField("pixelBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!;
                render.Invoke(form, [true]);
                var firstBuffer = buffer.GetValue(form);
                var firstPixels = pixels.GetValue(form);
                Assert.NotNull(firstBuffer);
                for (var i = 0; i < 50; i++) render.Invoke(form, [true]);
                Assert.Same(firstBuffer, buffer.GetValue(form));
                Assert.Same(firstPixels, pixels.GetValue(form));
                form.RenderSafely(() => throw new OutOfMemoryException());
                Assert.False(form.Visible);
                Assert.Null(buffer.GetValue(form));
                Assert.Empty(sheets);
                Assert.Empty((byte[])pixels.GetValue(form)!);
                var timer = (System.Windows.Forms.Timer)typeof(ArtemisDesktopPetForm)
                    .GetField("frameTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Assert.False(timer.Enabled);
                form.ShowNear(new Rectangle(0, 0, 1920, 1080));
                Assert.False(form.Visible);
                form.RenderSafely(() => throw new Exception("Must never retry"));
                // A queued VisibleChanged callback must also be harmless after failure.
                Application.DoEvents();
                form.Dispose();
                form.Dispose();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        Assert.Null(failure);
    }
}
