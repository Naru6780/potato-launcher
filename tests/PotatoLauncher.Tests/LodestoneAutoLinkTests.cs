namespace PotatoLauncher.Tests;

public class LodestoneAutoLinkTests
{
    [Fact]
    public void BuildLodestoneCharacterSearchUrl_UsesNameAndWorld()
    {
        var url = MainForm.BuildLodestoneCharacterSearchUrl("Artemis Potato", "Adamantoise");

        Assert.Equal("https://na.finalfantasyxiv.com/lodestone/character/?q=Artemis%20Potato&worldname=Adamantoise", url);
    }

    [Fact]
    public void TryFindExactLodestoneSearchCandidate_UsesExactNameAndWorld()
    {
        const string html = """
            <div class="entry"><a href="/lodestone/character/34875007/" class="entry__link">
            <div class="entry__chara__face"><img src="https://img2.finalfantasyxiv.com/f/sargatanas.jpg?1" alt="Artemis Potato"></div>
            <div class="entry__box entry__box--world"><p class="entry__name">Artemis Potato</p>
            <p class="entry__world"><i class="xiv-lds"></i>Sargatanas [Aether]</p></div></a></div>
            <div class="entry"><a href="/lodestone/character/32523285/" class="entry__link">
            <div class="entry__chara__face"><img src="https://img2.finalfantasyxiv.com/f/adamantoise.jpg?1" alt="Artemis Potato"></div>
            <div class="entry__box entry__box--world"><p class="entry__name">Artemis Potato</p>
            <p class="entry__world"><i class="xiv-lds"></i>Adamantoise [Aether]</p></div></a></div>
            """;

        var found = MainForm.TryFindExactLodestoneSearchCandidate(
            html,
            "Artemis Potato",
            "Adamantoise",
            out var candidate);

        Assert.True(found);
        Assert.Equal("32523285", candidate.LodestoneId);
        Assert.Equal("Artemis Potato", candidate.CharacterName);
        Assert.Equal("Adamantoise", candidate.World);
        Assert.Equal("https://eu.finalfantasyxiv.com/lodestone/character/32523285/", candidate.ProfileUrl);
        Assert.Equal("https://img2.finalfantasyxiv.com/f/adamantoise.jpg?1", candidate.IconUrl);
    }
}
