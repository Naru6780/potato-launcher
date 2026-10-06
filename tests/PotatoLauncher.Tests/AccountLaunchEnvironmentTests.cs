using System.Diagnostics;
using System.IO;
using PotatoLauncher;
using Xunit;

public sealed class AccountLaunchEnvironmentTests
{
    [Fact]
    public void AppliesVariablesOnlyForMatchingAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"launchEnv-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "musicapotato13-False-False": { "RESHADE_BASE_PATH_OVERRIDE": "C:\\Game\\ReShade-Artemis" } }""");
        try
        {
            var artemis = new ProcessStartInfo("x.exe");
            var other = new ProcessStartInfo("x.exe");
            other.Environment.Remove("RESHADE_BASE_PATH_OVERRIDE");

            Assert.Equal(1, AccountLaunchEnvironment.Apply(artemis, "MUSICAPOTATO13-False-False", path));
            Assert.Equal(0, AccountLaunchEnvironment.Apply(other, "musicapotato17-False-False", path));
            Assert.Equal("C:\\Game\\ReShade-Artemis", artemis.Environment["RESHADE_BASE_PATH_OVERRIDE"]);
            Assert.False(other.Environment.ContainsKey("RESHADE_BASE_PATH_OVERRIDE"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingOrInvalidFileAppliesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"launchEnv-{Guid.NewGuid():N}.json");
        Assert.Equal(0, AccountLaunchEnvironment.Apply(new ProcessStartInfo("x.exe"), "a", path));
        File.WriteAllText(path, "not json");
        try { Assert.Equal(0, AccountLaunchEnvironment.Apply(new ProcessStartInfo("x.exe"), "a", path)); }
        finally { File.Delete(path); }
    }
}
