namespace PotatoLauncher.Tests;

public class FrameLimitEnforcerTests
{
    [Theory]
    [InlineData(false, false, true, 60, FrameLimitEnforcer.Fps60)]   // follower: always the game's own limit
    [InlineData(false, true, true, 60, FrameLimitEnforcer.FpsNone)]  // main + NVIDIA cap at 60: exact 60 from the driver
    [InlineData(false, true, true, 0, FrameLimitEnforcer.Fps60)]     // NVIDIA cap off: never launch the main uncapped (121 FPS)
    [InlineData(false, true, true, null, FrameLimitEnforcer.Fps60)]  // no NVIDIA driver readable
    [InlineData(false, true, true, 144, FrameLimitEnforcer.Fps60)]   // NVIDIA cap not at the target
    [InlineData(false, true, false, 60, FrameLimitEnforcer.Fps60)]   // option turned off
    [InlineData(true, false, false, null, FrameLimitEnforcer.FpsNone)] // DLSS 5 clients keep their behaviour
    public void LaunchOption_GivesTheMainTheDriverCapOnlyWhenItExists(bool dlss5, bool isMain, bool mainUsesDriverCap, int? driverCap, int expected) =>
        Assert.Equal(expected, FrameLimitEnforcer.LaunchOption(dlss5, isMain, mainUsesDriverCap, driverCap, 60));

    private const string Sample = "\r\n<FINAL FANTASY XIV Config File>\r\n\r\n<Version>\r\nVersion\t1.0\r\n\r\n<Settings>\r\nScreenMode\t0\r\nFps\t0\r\nFPSInActive\t0\r\nFpsSomethingElse\t7\r\n";

    [Fact]
    public void TargetMapsToTheGamesBuiltInOptions()
    {
        Assert.Equal(FrameLimitEnforcer.Fps30, FrameLimitEnforcer.OptionForTarget(30));
        Assert.Equal(FrameLimitEnforcer.Fps60, FrameLimitEnforcer.OptionForTarget(60));
        Assert.Equal(FrameLimitEnforcer.Fps60, FrameLimitEnforcer.OptionForTarget(45));
        Assert.Equal(FrameLimitEnforcer.FpsRefreshRate, FrameLimitEnforcer.OptionForTarget(120));
    }

    [Fact]
    public void ReplacesOnlyTheFpsValueAndKeepsTheRestByteForByte()
    {
        var updated = FrameLimitEnforcer.SetOption(Sample, "Fps", 2, out var previous);
        Assert.Equal(0, previous);
        Assert.Equal(Sample.Replace("Fps\t0\r\n", "Fps\t2\r\n"), updated);
        Assert.Contains("FpsSomethingElse\t7", updated);
        Assert.Contains("FPSInActive\t0", updated);

        FrameLimitEnforcer.SetOption("Other\t1\r\n", "Fps", 2, out var missing);
        Assert.Equal(-1, missing);
    }

    [Fact]
    public void ApplyWritesTheFileOnlyWhenNeeded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ffxiv-{Guid.NewGuid():N}.cfg");
        File.WriteAllText(path, Sample);
        try
        {
            Assert.Contains("set to 60 fps", FrameLimitEnforcer.Apply(60, path));
            Assert.Equal(2, FrameLimitEnforcer.CurrentOption(path));
            var stamp = File.GetLastWriteTimeUtc(path);
            Assert.Equal("", FrameLimitEnforcer.Apply(60, path));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            Assert.Equal("", FrameLimitEnforcer.Apply(60, path + ".missing"));
        }
        finally { File.Delete(path); }
    }
}

public class FrameLimitEnforcerOptionTests
{
    [Fact]
    public void DlssClientsGetNoneAndOthersGetTheTargetOption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ffxiv-{Guid.NewGuid():N}.cfg");
        File.WriteAllText(path, "<Settings>\r\nFps\t2\r\n");
        try
        {
            Assert.Contains("set to none", FrameLimitEnforcer.ApplyOption(FrameLimitEnforcer.FpsNone, path));
            Assert.Equal(0, FrameLimitEnforcer.CurrentOption(path));
            Assert.Contains("set to 60 fps", FrameLimitEnforcer.Apply(60, path));
            Assert.Equal(2, FrameLimitEnforcer.CurrentOption(path));
        }
        finally { File.Delete(path); }
    }
}
