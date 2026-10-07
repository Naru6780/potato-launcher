using System.Diagnostics;

namespace PotatoLauncher.Tests;

public class ClientFpsTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FpsIsFramesOverWallTimeWithGameRateAsFirstValue()
    {
        var tracker = new ClientFpsTracker();
        Assert.Equal(59.9, tracker.Update(1, new FrameSample(1000, 59.9f, T0)));
        Assert.Equal(59.9, tracker.Update(1, new FrameSample(1010, 59.9f, T0.AddSeconds(0.2)))); // too soon: keep previous
        Assert.Equal(48.0, tracker.Update(1, new FrameSample(1048, 60f, T0.AddSeconds(1))));
        Assert.Equal(60.0, tracker.Update(1, new FrameSample(1108, 60f, T0.AddSeconds(2))));
        Assert.Null(tracker.Update(1, null));
    }

    [Fact]
    public void CounterWrapAroundIsHandled()
    {
        var tracker = new ClientFpsTracker();
        tracker.Update(1, new FrameSample(uint.MaxValue - 9, null, T0));
        Assert.Equal(60.0, tracker.Update(1, new FrameSample(50, null, T0.AddSeconds(1))));
    }

    // Reads real running clients when there are any (on a dev PC with the supported game build); otherwise a no-op.
    [Fact]
    public void ReadsLiveClientsWhenAvailable()
    {
        var reader = new ExternalGameState();
        var tracker = new ClientFpsTracker();
        foreach (var process in Process.GetProcessesByName("ffxiv_dx11"))
        {
            using (process)
            {
                var start = process.StartTime.ToUniversalTime();
                var first = reader.ReadFrame(process.Id, start);
                if (first is null) continue; // unsupported build or access denied
                tracker.Update(process.Id, first);
                Thread.Sleep(1000);
                var fps = tracker.Update(process.Id, reader.ReadFrame(process.Id, start));
                Assert.NotNull(fps);
                Assert.InRange(fps!.Value, 5, 1000);
                return;
            }
        }
    }
}
