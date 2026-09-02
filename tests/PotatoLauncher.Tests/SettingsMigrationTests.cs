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
          "MusicMuted": true,
          "StopMusicWhenAllLoaded": true,
          "MusicVolume": 80,
          "UnexpectedFutureConflict": true
        }
        """;

        var cleanedJson = SettingsMigration.CleanSettingsJson(dirtyJson, out var changed);
        using var document = JsonDocument.Parse(cleanedJson);

        Assert.True(changed);
        Assert.Equal("C:\\Launchers", document.RootElement.GetProperty("DalamudFolder").GetString());
        Assert.Equal("Shared", document.RootElement.GetProperty("LaunchMode").GetString());
        Assert.True(document.RootElement.GetProperty("NotificationsEnabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("DesktopPetEnabled").GetBoolean());
        Assert.Equal(ArtemisPetScale.DefaultPercent, document.RootElement.GetProperty("DesktopPetSizePercent").GetInt32());
        Assert.False(document.RootElement.GetProperty("WaitForClientInitializationBeforeNextLaunch").GetBoolean());
        Assert.False(document.RootElement.GetProperty("AutoCloseLaunchHelpers").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("SharedAccountOrder", out _));
        Assert.False(document.RootElement.TryGetProperty("InstancedAccountOrder", out _));
        Assert.False(document.RootElement.TryGetProperty("LastConnectedUtc", out _));
        Assert.False(document.RootElement.TryGetProperty("MusicMuted", out _));
        Assert.False(document.RootElement.TryGetProperty("StopMusicWhenAllLoaded", out _));
        Assert.False(document.RootElement.TryGetProperty("MusicVolume", out _));
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
        Assert.Equal(AccountSortModes.Custom, state.SharedSortMode);
        Assert.Equal(AccountSortModes.Custom, state.InstancedSortMode);
    }

    [Fact]
    public void CleanAccountListState_NormalizesPersistedSortModes()
    {
        var state = new AccountListState
        {
            SharedSortMode = "selectedband",
            InstancedSortMode = "unknown"
        };

        var changed = SettingsMigration.CleanAccountListState(state);

        Assert.True(changed);
        Assert.Equal(AccountSortModes.SelectedBand, state.SharedSortMode);
        Assert.Equal(AccountSortModes.Custom, state.InstancedSortMode);
    }

    [Fact]
    public void CleanSettingsJson_TrimsAccountIconProfilesAndRemovesEmptyEntries()
    {
        var dirtyJson = """
        {
          "AccountIcons": {
            "  alpha  ": {
              "CharacterName": "  Alpha  ",
              "World": "  Balmung  ",
              "LodestoneId": " 12345 ",
              "ProfileUrl": " https://na.finalfantasyxiv.com/lodestone/character/12345/ ",
              "IconUrl": " https://img.finalfantasyxiv.com/alpha.png ",
              "IconFileName": " alpha-face.png ",
              "FullImageUrl": " https://img.finalfantasyxiv.com/alpha-full.png ",
              "FullImageFileName": " alpha-full.png "
            },
            "empty": {
              "CharacterName": " ",
              "World": "",
              "LodestoneId": "",
              "ProfileUrl": " ",
              "IconUrl": "",
              "IconFileName": "",
              "FullImageUrl": "",
              "FullImageFileName": ""
            }
          }
        }
        """;

        var cleanedJson = SettingsMigration.CleanSettingsJson(dirtyJson, out var changed);
        using var document = JsonDocument.Parse(cleanedJson);
        var accountIcons = document.RootElement.GetProperty("AccountIcons");

        Assert.True(changed);
        Assert.True(accountIcons.TryGetProperty("alpha", out var profile));
        Assert.False(accountIcons.TryGetProperty("  alpha  ", out _));
        Assert.False(accountIcons.TryGetProperty("empty", out _));
        Assert.Equal("Alpha", profile.GetProperty("CharacterName").GetString());
        Assert.Equal("Balmung", profile.GetProperty("World").GetString());
        Assert.Equal("12345", profile.GetProperty("LodestoneId").GetString());
        Assert.Equal("alpha-face.png", profile.GetProperty("IconFileName").GetString());
    }

    [Fact]
    public void CleanSettingsJson_AssignsStableUniqueBandIds()
    {
        var duplicateId = Guid.NewGuid().ToString("N");
        var dirtyJson = $$"""
        {
          "InstancedBands": [
            { "Name": "One", "BatchFiles": [], "Id": "{{duplicateId}}" },
            { "Name": "Two", "BatchFiles": [], "Id": "{{duplicateId}}" },
            { "Name": "Three", "BatchFiles": [] }
          ]
        }
        """;

        var cleanedJson = SettingsMigration.CleanSettingsJson(dirtyJson, out var changed);
        var settings = JsonSerializer.Deserialize<AppSettings>(cleanedJson)!;
        var ids = settings.InstancedBands.Select(band => band.Id).ToList();

        Assert.True(changed);
        Assert.Equal(3, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _)));
    }
}
