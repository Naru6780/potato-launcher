namespace PotatoLauncher.Tests;

public class OptimizerDiagnosticsTests
{
    [Fact]
    public void CountsOnlyEnabledCollectionsNotBoundToCharacters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dalamud-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        { "SavedProfiles": { "$values": [
          { "n": "Quest", "e": true, "pc": { "$values": [] }, "Plugins": { "$values": [
              { "InternalName": "vnavmesh", "IsEnabled": true }, { "InternalName": "BossMod", "IsEnabled": false } ] } },
          { "n": "Bands", "e": true, "pc": { "$values": [] }, "Plugins": { "$values": [
              { "InternalName": "vnavmesh", "IsEnabled": true }, { "InternalName": "QoLBar", "IsEnabled": true } ] } },
          { "n": "Mine", "e": true, "pc": { "$values": [ { "id": 1 } ] }, "Plugins": { "$values": [ { "InternalName": "Glamourer", "IsEnabled": true } ] } },
          { "n": "Off", "e": false, "pc": { "$values": [] }, "Plugins": { "$values": [ { "InternalName": "Penumbra", "IsEnabled": true } ] } }
        ] } }
        """);
        try
        {
            var (names, plugins) = OptimizerDiagnostics.GloballyEnabledDalamudCollections(path);
            Assert.Equal(["Quest", "Bands"], names);
            Assert.Equal(2, plugins); // vnavmesh counted once, disabled BossMod not counted
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReportsClientsBelowTheTarget()
    {
        var clients = new List<OptimizerClientSnapshot>
        {
            new(1, "A@W", "", false, false, 5, null, 0, 0, 0, 0, null, 60),
            new(2, "B@W", "", false, false, 5, null, 0, 0, 0, 0, null, 41)
        };
        Assert.Contains(OptimizerDiagnostics.Get(clients, 60), finding => finding.StartsWith("1 client is below 60 FPS: B@W"));
    }

    [Fact]
    public void FlagsTheDlssLoaderWithTheInGameLimitAndOtherProgramsChangingClients()
    {
        var clients = new List<OptimizerClientSnapshot>
        {
            new(1, "Artemis@W", "", false, false, 8, null, 0, 0, 0, 0, null, 46, "Main", 60),
            new(2, "Hermes@W", "", false, false, 3, null, 0, 0, 0, 0, null, 59, "Background", 60)
        };
        var findings = OptimizerDiagnostics.Evaluate(clients, 60, (Priority: 6, Affinity: 2), new HashSet<int> { 1 }, []);
        Assert.Contains(findings, finding => finding.StartsWith("Artemis@W loads the DLSS 5 add-on and has the in-game 60 fps limit"));
        Assert.DoesNotContain(findings, finding => finding.StartsWith("Hermes@W loads"));
        Assert.Contains(findings, finding => finding.StartsWith("Another program changed client priorities 6 times"));
        Assert.Contains(findings, finding => finding.StartsWith("Another program keeps changing client CPU cores"));

        // A DLSS client run the intended way (in-game None, driver cap) is not flagged.
        var intended = new List<OptimizerClientSnapshot> { new(1, "Artemis@W", "", false, false, 8, null, 0, 0, 0, 0, null, 59, "Main", 0) };
        Assert.DoesNotContain(OptimizerDiagnostics.Evaluate(intended, 60, default, new HashSet<int> { 1 }, []), finding => finding.Contains("DLSS 5 add-on"));
    }
}
