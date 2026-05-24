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
}
