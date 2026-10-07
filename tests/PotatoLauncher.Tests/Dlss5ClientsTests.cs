using System.Diagnostics;
using System.IO;
using PotatoLauncher;
using Xunit;

public sealed class Dlss5ClientsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"dlss5-{Guid.NewGuid():N}");
    private string Game => Path.Combine(root, "game");
    private string Data => Path.Combine(root, "data");

    public Dlss5ClientsTests()
    {
        Directory.CreateDirectory(Game);
        File.WriteAllText(Path.Combine(Game, "ffxiv_dx11.exe"), "");
        File.WriteAllText(Path.Combine(Game, "ReShade.ini"),
            "[ADDON]\r\nDisabledAddons=Generic Depth\r\n\r\n[GENERAL]\r\nEffectSearchPaths=.\\reshade-shaders\\Shaders,.\\reshade-shaders\\Shaders\\qUINT\r\nPresetPath=.\\ReShadePreset.ini\r\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    [Fact]
    public void SetIniValue_ReplacesInsertsAndAddsSection()
    {
        var ini = "[ADDON]\r\nAddonPath=old\r\n[GENERAL]\r\nX=1";
        Assert.Equal("[ADDON]\r\nAddonPath=new\r\n[GENERAL]\r\nX=1", Dlss5Clients.SetIniValue(ini, "ADDON", "AddonPath", "new"));
        Assert.Equal("[ADDON]\r\nAddonPath=new\r\nY=2", Dlss5Clients.SetIniValue("[ADDON]\r\nY=2", "addon", "AddonPath", "new"));
        Assert.Contains("[ADDON]\nAddonPath=new", Dlss5Clients.SetIniValue("[GENERAL]\nX=1\n", "ADDON", "AddonPath", "new"));
    }

    [Fact]
    public void SetIniValue_DoesNotTouchSameKeyInOtherSection()
    {
        var ini = "[OTHER]\nAddonPath=keep\n[ADDON]\nZ=1";
        var result = Dlss5Clients.SetIniValue(ini, "ADDON", "AddonPath", "new");
        Assert.Contains("[OTHER]\nAddonPath=keep", result);
        Assert.Contains("[ADDON]\nAddonPath=new\nZ=1", result);
    }

    [Fact]
    public void AbsolutizeRelativePaths_RewritesOnlyDotPrefixedValues()
    {
        var result = Dlss5Clients.AbsolutizeRelativePaths("A=.\\x,.\\y\nB=C:\\z\nC=..\\w", @"D:\Game");
        Assert.Equal("A=D:\\Game\\x,D:\\Game\\y\nB=C:\\z\nC=..\\w", result);
    }

    [Fact]
    public void EnsureSplit_RequiresAnAddOn()
    {
        Assert.Contains("add-on", Dlss5Clients.EnsureSplit(Game, Data));
    }

    [Fact]
    public void EnsureSplit_PointsSharedIniAtEmptyFolderAndDlssIniAtGame()
    {
        File.WriteAllText(Path.Combine(Game, "renodx-dlss.addon64"), "");

        Assert.Equal("", Dlss5Clients.EnsureSplit(Game, Data));

        var shared = File.ReadAllText(Path.Combine(Game, "ReShade.ini"));
        var dlss = File.ReadAllText(Path.Combine(Dlss5Clients.DlssProfileFolder(Data), "ReShade.ini"));
        Assert.Contains($"AddonPath={Dlss5Clients.NoAddOnsFolder(Data)}", shared);
        Assert.Contains(".\\reshade-shaders", shared);
        Assert.Contains($"AddonPath={Game}", dlss);
        Assert.Contains($"PresetPath={Game}\\ReShadePreset.ini", dlss);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Dlss5Clients.NoAddOnsFolder(Data)));

        // Re-applying keeps user changes in the DLSS profile and heals an installer-reset shared ini.
        File.AppendAllText(Path.Combine(Dlss5Clients.DlssProfileFolder(Data), "ReShade.ini"), "\r\n[MINE]\r\nKeep=1\r\n");
        File.WriteAllText(Path.Combine(Game, "ReShade.ini"), "[ADDON]\r\nDisabledAddons=\r\n");
        Assert.Equal("", Dlss5Clients.EnsureSplit(Game, Data));
        Assert.Contains("Keep=1", File.ReadAllText(Path.Combine(Dlss5Clients.DlssProfileFolder(Data), "ReShade.ini")));
        Assert.Contains($"AddonPath={Dlss5Clients.NoAddOnsFolder(Data)}", File.ReadAllText(Path.Combine(Game, "ReShade.ini")));
    }

    [Fact]
    public void ConfigRoundTripsAndMatchesAccountsCaseInsensitively()
    {
        Dlss5Clients.Save(new Dlss5Config { AccountKeys = ["Artemis-False-False", "artemis-false-false", " "] }, Data);
        var loaded = Dlss5Clients.Load(Data);
        Assert.Single(loaded.AccountKeys);
        Assert.True(Dlss5Clients.IsEnabled(loaded, "ARTEMIS-False-False"));
        Assert.False(Dlss5Clients.IsEnabled(loaded, "hermes-False-False"));
    }

    [Fact]
    public void ReShadeOff_StillAppliesWhenNoDlssClientIsSelected()
    {
        var none = new Dlss5Config { AccountKeys = [], ReShadeOffForOtherClients = true };
        var startInfo = new ProcessStartInfo("x.exe");
        Assert.False(Dlss5Clients.ApplyToLaunch(startInfo, "any-False-False", none, Game, Data, out var message));
        Assert.Equal("", message);
        Assert.Equal("1", startInfo.Environment["RESHADE_DISABLE_GRAPHICS_HOOK"]);
        Assert.Equal(Dlss5Clients.ReShadeOffFolder(Data), startInfo.Environment["RESHADE_BASE_PATH_OVERRIDE"]);

        // Opted out: nothing is touched.
        var untouched = new ProcessStartInfo("x.exe");
        untouched.Environment.Remove("RESHADE_DISABLE_GRAPHICS_HOOK");
        Assert.False(Dlss5Clients.ApplyToLaunch(untouched, "any-False-False", new Dlss5Config { AccountKeys = [], ReShadeOffForOtherClients = false }, Game, Data, out _));
        Assert.False(untouched.Environment.ContainsKey("RESHADE_DISABLE_GRAPHICS_HOOK"));

        // Without ReShade in the game folder there is nothing to switch off.
        File.Delete(Path.Combine(Game, "ReShade.ini"));
        var noReShade = new ProcessStartInfo("x.exe");
        noReShade.Environment.Remove("RESHADE_DISABLE_GRAPHICS_HOOK");
        Assert.False(Dlss5Clients.ApplyToLaunch(noReShade, "any-False-False", none, Game, Data, out _));
        Assert.False(noReShade.Environment.ContainsKey("RESHADE_DISABLE_GRAPHICS_HOOK"));
    }

    [Fact]
    public void ReShadeOff_SetsAllSwitchesOnlyWithTheForwardingProfile()
    {
        var startInfo = new ProcessStartInfo("x.exe");
        Assert.True(Dlss5Clients.TryApplyReShadeOff(startInfo, Data));

        var folder = Dlss5Clients.ReShadeOffFolder(Data);
        Assert.Equal(folder, startInfo.Environment["RESHADE_BASE_PATH_OVERRIDE"]);
        Assert.Equal("1", startInfo.Environment["RESHADE_DISABLE_GRAPHICS_HOOK"]);
        Assert.Equal("1", startInfo.Environment["RESHADE_DISABLE_INPUT_HOOK"]);
        var ini = File.ReadAllText(Path.Combine(folder, "ReShade.ini"));
        Assert.Contains("EnableProxyLibrary=1", ini);
        Assert.Contains($"ProxyLibrary={Path.Combine(Environment.SystemDirectory, "dxgi.dll")}", ini);
        Assert.Contains("Logging=0", ini);
    }

    [Fact]
    public void FindGameFolder_UsesValidOverride()
    {
        Assert.Equal(Path.GetFullPath(Game), Dlss5Clients.FindGameFolder(new Dlss5Config { GameFolderOverride = Game }, ""));
    }
}
