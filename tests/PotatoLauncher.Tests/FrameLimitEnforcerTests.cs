namespace PotatoLauncher.Tests;

public class FrameLimitEnforcerTests
{
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
