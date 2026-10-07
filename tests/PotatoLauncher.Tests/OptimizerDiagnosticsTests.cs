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
            new(1, "A@W", "", false, false, 5, null, 0, 0, 0, 0, null, null, false, null, 60),
            new(2, "B@W", "", false, false, 5, null, 0, 0, 0, 0, null, null, false, null, 41)
        };
        Assert.Contains(OptimizerDiagnostics.Get(clients, 60), finding => finding.StartsWith("1 client is below 60 FPS: B@W"));
    }
}
