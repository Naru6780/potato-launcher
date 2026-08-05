using System.Drawing;

namespace PotatoLauncher.Tests;

public class ArtemisWelcomeTests
{
    [Fact]
    public void CuteQuotes_AreDistinctShortAndLoreInspired()
    {
        Assert.True(ArtemisWelcomeContent.CuteQuotes.Length >= 8);
        Assert.Equal(
            ArtemisWelcomeContent.CuteQuotes.Length,
            ArtemisWelcomeContent.CuteQuotes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ArtemisWelcomeContent.CuteQuotes, quote =>
        {
            Assert.False(string.IsNullOrWhiteSpace(quote));
            Assert.InRange(quote.Length, 10, 80);
        });
        Assert.Contains(ArtemisWelcomeContent.CuteQuotes, quote =>
            quote.Contains("Eorzea", StringComparison.Ordinal) ||
            quote.Contains("Crystal", StringComparison.Ordinal) ||
            quote.Contains("chocobo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceFrameBounds_TileTheWholeSheetWithoutGaps()
    {
        var sheetSize = new Size(1458, 1080);
        var frames = Enumerable.Range(0, ArtemisSpriteSheetLayout.FrameCount)
            .Select(index => ArtemisSpriteSheetLayout.SourceFrameBounds(sheetSize, index))
            .ToArray();

        Assert.All(frames, frame =>
        {
            Assert.True(frame.Width > 0);
            Assert.True(frame.Height > 0);
            Assert.True(frame.Left >= 0 && frame.Top >= 0);
            Assert.True(frame.Right <= sheetSize.Width && frame.Bottom <= sheetSize.Height);
        });
        Assert.Equal(sheetSize.Width * sheetSize.Height, frames.Sum(frame => frame.Width * frame.Height));
    }

    [Fact]
    public void WelcomeTimeline_PlaysIdleWaveAndSettlesOverThreeSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), ArtemisWelcomeTimeline.Duration);
        Assert.Equal(ArtemisAnimationState.Idle, ArtemisWelcomeTimeline.FrameAt(TimeSpan.Zero).State);
        Assert.Equal(ArtemisAnimationState.Wave, ArtemisWelcomeTimeline.FrameAt(TimeSpan.FromSeconds(1)).State);
        Assert.Equal(ArtemisAnimationState.Idle, ArtemisWelcomeTimeline.FrameAt(TimeSpan.FromSeconds(2.7)).State);
        Assert.InRange(
            ArtemisWelcomeTimeline.FrameAt(TimeSpan.FromSeconds(3)).FrameIndex,
            0,
            ArtemisSpriteSheetLayout.FrameCount - 1);
    }

    [Fact]
    public void DesktopPetTiming_LoopsMovementAndCompletesRelease()
    {
        var fullRun = TimeSpan.FromMilliseconds(
            ArtemisAnimationTiming.FrameDurationMilliseconds(ArtemisAnimationState.Run) * ArtemisAnimationTiming.FrameCount);
        Assert.Equal(0, ArtemisAnimationTiming.FrameAt(ArtemisAnimationState.Run, fullRun));

        var releaseDuration = TimeSpan.FromMilliseconds(
            ArtemisAnimationTiming.FrameDurationMilliseconds(ArtemisAnimationState.Release) * ArtemisAnimationTiming.FrameCount);
        Assert.False(ArtemisAnimationTiming.IsComplete(ArtemisAnimationState.Release, releaseDuration - TimeSpan.FromMilliseconds(1)));
        Assert.True(ArtemisAnimationTiming.IsComplete(ArtemisAnimationState.Release, releaseDuration));
        Assert.Equal(
            ArtemisAnimationTiming.FrameCount - 1,
            ArtemisAnimationTiming.FrameAt(ArtemisAnimationState.Release, releaseDuration));
    }
}
