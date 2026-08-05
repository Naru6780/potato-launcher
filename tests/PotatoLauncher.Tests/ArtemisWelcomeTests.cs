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
    public void FitFrameBounds_PreservesTheAtlasCellAspectRatio()
    {
        var destination = ArtemisSpriteSheetLayout.FitFrameBounds(
            new Size(420, 340),
            new Rectangle(35, 30, 340, 300));

        Assert.Equal(340, destination.Width);
        Assert.Equal(275, destination.Height);
        Assert.InRange(
            Math.Abs(destination.Width / (double)destination.Height - 420D / 340D),
            0,
            0.002);
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

    [Fact]
    public void DesktopPetAtlases_KeepEveryFrameAtOneCharacterHeight()
    {
        var atlasPaths = new[]
        {
            ArtemisAnimationAssets.Idle,
            ArtemisAnimationAssets.Run,
            ArtemisAnimationAssets.Release,
            ArtemisAnimationAssets.Wave
        };

        foreach (var atlasPath in atlasPaths)
        {
            using var atlas = new Bitmap(atlasPath);
            for (var frameIndex = 0; frameIndex < ArtemisSpriteSheetLayout.FrameCount; frameIndex++)
            {
                var frame = ArtemisSpriteSheetLayout.SourceFrameBounds(atlas.Size, frameIndex);
                var top = frame.Bottom;
                var bottom = frame.Top - 1;
                for (var y = frame.Top; y < frame.Bottom; y++)
                {
                    for (var x = frame.Left; x < frame.Right; x++)
                    {
                        if (atlas.GetPixel(x, y).A == 0) continue;
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }

                Assert.Equal(300, bottom - top + 1);
            }
        }
    }
}
