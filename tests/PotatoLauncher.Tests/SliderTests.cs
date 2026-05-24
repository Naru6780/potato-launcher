namespace PotatoLauncher.Tests;

public class SliderTests
{
    [Fact]
    public void ThemeSliderMetrics_AreCompact()
    {
        Assert.True(ThemeSliderMetrics.Height <= 30);
        Assert.True(ThemeSliderMetrics.TrackHeight <= 8);
        Assert.True(ThemeSliderMetrics.ThumbRadius <= 9);
    }

    [Fact]
    public void ThemeSlider_PaletteSetter_KeepsSupportedBackColor()
    {
        using var slider = new ThemeSlider();

        var exception = Record.Exception(() => slider.Palette = MainForm.Palettes["Dark"]);

        Assert.Null(exception);
        Assert.NotEqual(System.Drawing.Color.Transparent, slider.BackColor);
    }
}
