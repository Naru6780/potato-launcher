using System.Text.Json;

namespace PotatoLauncher.Tests;

public class SettingsMigrationTests
{
    [Fact]
    public void CleanSettingsJson_RemovesObsoleteAndUnknownTopLevelValues()
    {
        var dirtyJson = """
        {
          "DalamudFolder": "C:\\Launchers",
          "LaunchMode": "Shared XIVLauncher",
          "SharedAccountOrder": ["old"],
          "InstancedAccountOrder": ["old"],
          "LastConnectedUtc": { "old": "2026-01-01T00:00:00Z" },
          "UnexpectedFutureConflict": true
        }
        """;

        var cleanedJson = SettingsMigration.CleanSettingsJson(dirtyJson, out var changed);
        using var document = JsonDocument.Parse(cleanedJson);

        Assert.True(changed);
        Assert.Equal("C:\\Launchers", document.RootElement.GetProperty("DalamudFolder").GetString());
        Assert.Equal("Shared", document.RootElement.GetProperty("LaunchMode").GetString());
        Assert.False(document.RootElement.TryGetProperty("SharedAccountOrder", out _));
        Assert.False(document.RootElement.TryGetProperty("InstancedAccountOrder", out _));
        Assert.False(document.RootElement.TryGetProperty("LastConnectedUtc", out _));
        Assert.False(document.RootElement.TryGetProperty("UnexpectedFutureConflict", out _));
    }

    [Fact]
    public void CleanAccountListState_RemovesBlankAndDuplicatePersistedValues()
    {
        var state = new AccountListState
        {
            SharedAccountOrder = ["alpha", "", "alpha", "beta"],
            InstancedAccountOrder = ["", "bat-one", "BAT-ONE", "bat-two"],
            LastConnectedUtc =
            {
                [""] = DateTime.UtcNow,
                ["alpha"] = DateTime.UtcNow
            }
        };

        var changed = SettingsMigration.CleanAccountListState(state);

        Assert.True(changed);
        Assert.Equal(["alpha", "beta"], state.SharedAccountOrder);
        Assert.Equal(["bat-one", "bat-two"], state.InstancedAccountOrder);
        Assert.DoesNotContain("", state.LastConnectedUtc.Keys);
    }
}
