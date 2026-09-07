using System.Drawing;
using System.Drawing.Imaging;

namespace PotatoLauncher.Tests;

public class NewsBandrollTests
{
    [Theory]
    [InlineData(600, 180, 780, 60)]
    [InlineData(600, 180, 304, 52)]
    [InlineData(600, 180, 72, 32)]
    [InlineData(160, 160, 304, 52)]
    [InlineData(120, 600, 304, 52)]
    [InlineData(2000, 80, 304, 52)]
    public void FitKeepsFullImageProportionalAndCentered(int imageWidth, int imageHeight, int width, int height)
    {
        var bounds = new Rectangle(6, 5, width - 12, height - 10);
        var target = NewsBandrollControl.FitImageRectangle(new Size(imageWidth, imageHeight), bounds);
        Assert.True(target.Width > 0 && target.Height > 0);
        Assert.InRange(target.Left, bounds.Left - 0.001F, bounds.Right);
        Assert.InRange(target.Right, bounds.Left, bounds.Right + 0.001F);
        Assert.InRange(target.Top, bounds.Top - 0.001F, bounds.Bottom);
        Assert.InRange(target.Bottom, bounds.Top, bounds.Bottom + 0.001F);
        Assert.Equal(imageWidth / (float)imageHeight, target.Width / target.Height, 3);
        Assert.Equal(bounds.Left + bounds.Width / 2F, target.Left + target.Width / 2F, 3);
        Assert.Equal(bounds.Top + bounds.Height / 2F, target.Top + target.Height / 2F, 3);
    }

    [Theory]
    [InlineData(0, 100, 300, 40)]
    [InlineData(100, 0, 300, 40)]
    [InlineData(100, 100, 0, 40)]
    [InlineData(100, 100, 300, 0)]
    public void EmptyDimensionsDoNotProduceAnInvalidDrawRectangle(int imageWidth, int imageHeight, int width, int height)
    {
        Assert.Equal(RectangleF.Empty, NewsBandrollControl.FitImageRectangle(new Size(imageWidth, imageHeight), new Rectangle(0, 0, width, height)));
    }

    [Theory]
    [InlineData(780, 60, 600, 180)]
    [InlineData(304, 52, 600, 180)]
    [InlineData(304, 52, 1000, 80)]
    [InlineData(304, 52, 160, 160)]
    [InlineData(440, 104, 600, 180)]
    [InlineData(440, 67, 600, 180)]
    public void ActualControlPaintPreservesAllFourImageCornersAndClick(int width, int height, int imageWidth, int imageHeight)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var control = new NewsBandrollControl { BackColor = Color.DarkMagenta };
                control.SetLayoutBounds(new Rectangle(30, 20, width, height));
                var fixture = new Bitmap(imageWidth, imageHeight);
                using (var graphics = Graphics.FromImage(fixture))
                {
                    graphics.Clear(Color.White);
                    graphics.FillRectangle(Brushes.Red, 0, 0, imageWidth / 5, imageHeight / 3);
                    graphics.FillRectangle(Brushes.Lime, imageWidth * 4 / 5, 0, imageWidth / 5, imageHeight / 3);
                    graphics.FillRectangle(Brushes.Blue, 0, imageHeight * 2 / 3, imageWidth / 5, imageHeight / 3 + 1);
                    graphics.FillRectangle(Brushes.Gold, imageWidth * 4 / 5, imageHeight * 2 / 3, imageWidth / 5, imageHeight / 3 + 1);
                    using var font = new Font("Arial", Math.Max(8, imageHeight / 10));
                    graphics.DrawString("FULL NEWS IMAGE", font, Brushes.Black, imageWidth / 5, imageHeight / 3);
                }
                control.SetSlides([new NewsBandrollSlide(fixture, "https://example.com/news", "Fixture")]);
                using var rendered = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(rendered, control.ClientRectangle);
                var target = Rectangle.Round(NewsBandrollControl.FitImageRectangle(fixture.Size, new Rectangle(6, 5, control.Width - 12, control.Height - 10)));
                Assert.InRange(control.Width - target.Width, 12, 13);
                Assert.InRange(control.Height - target.Height, 10, 11);
                Assert.Equal(Color.DarkMagenta.ToArgb(), rendered.GetPixel(0, control.Height / 2).ToArgb());
                AssertPixel(rendered, target, 0.1F, 0.15F, Color.Red);
                AssertPixel(rendered, target, 0.9F, 0.15F, Color.Lime);
                AssertPixel(rendered, target, 0.1F, 0.85F, Color.Blue);
                AssertPixel(rendered, target, 0.9F, 0.85F, Color.Gold);

                string? clickedUrl = null;
                control.ItemClicked += (_, url) => clickedUrl = url;
                typeof(NewsBandrollControl).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(control, [EventArgs.Empty]);
                Assert.Equal("https://example.com/news", clickedUrl);

                // Optional local visual inspection; never captures the desktop or user data.
                var previewDirectory = Environment.GetEnvironmentVariable("POTATO_BANDROLL_TEST_PREVIEWS");
                if (!string.IsNullOrWhiteSpace(previewDirectory))
                {
                    Directory.CreateDirectory(previewDirectory);
                    rendered.Save(Path.Combine(previewDirectory, $"bandroll-{width}x{height}-{imageWidth}x{imageHeight}.png"), ImageFormat.Png);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Bandroll rendering test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void LayoutTracksImageShapeOnSlideChangeAndWindowResize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var control = new NewsBandrollControl();
                var slot = new Rectangle(600, 24, 304, 52);
                control.SetLayoutBounds(slot);
                control.SetSlides([
                    new NewsBandrollSlide(new Bitmap(600, 180), "https://example.com/wide", "Wide"),
                    new NewsBandrollSlide(new Bitmap(160, 160), "https://example.com/square", "Square")]);
                Assert.Equal(152, control.Width);
                Assert.Equal(52, control.Height);
                Assert.True(slot.Contains(control.Bounds));

                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(NewsBandrollControl).GetField("animating", flags)!.SetValue(control, true);
                typeof(NewsBandrollControl).GetField("animationStartUtc", flags)!.SetValue(control, DateTime.UtcNow.AddSeconds(-2));
                typeof(NewsBandrollControl).GetMethod("UpdateRollAnimation", flags)!.Invoke(control, null);
                Assert.Equal(54, control.Width);
                Assert.Equal(52, control.Height);
                Assert.True(slot.Contains(control.Bounds));
                string? url = null;
                control.ItemClicked += (_, clickedUrl) => url = clickedUrl;
                typeof(NewsBandrollControl).GetMethod("OnClick", flags)!.Invoke(control, [EventArgs.Empty]);
                Assert.Equal("https://example.com/square", url);

                var compactSlot = new Rectangle(600, 24, 72, 32);
                control.SetLayoutBounds(compactSlot);
                Assert.Equal(34, control.Width);
                Assert.Equal(32, control.Height);
                Assert.True(compactSlot.Contains(control.Bounds));
                control.SetSlides([]);
                Assert.False(control.HasSlides);
                Assert.Equal(compactSlot, control.Bounds);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    private static void AssertPixel(Bitmap bitmap, Rectangle target, float x, float y, Color expected)
    {
        var actual = bitmap.GetPixel(target.Left + (int)(target.Width * x), target.Top + (int)(target.Height * y));
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 10);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 10);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 10);
    }
}
