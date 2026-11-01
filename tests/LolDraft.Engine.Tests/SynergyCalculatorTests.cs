using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

public sealed class SynergyCalculatorTests
{
    [Fact]
    public void Frontline_engage_dengeli_hasar_ve_kombo_puan_alir()
    {
        // Renekton/Leona frontline +3, engage +2, AP payı 0.4 +2, Leona+Draven kombo +2
        Assert.Equal(9, SynergyCalculator.Score(TestTeams.EarlyBlue()));
    }

    [Fact]
    public void Kombo_sadece_iki_sampiyon_birlikteyse_sayilir()
    {
        var blue = TestTeams.EarlyBlue();
        var withoutDraven = new TeamDraft(blue.Top, blue.Jungle, blue.Mid,
            new ChampionProfile("jinx2", "Jinx", 9, 6, 5, frontline: 0, engage: 0, apShare: 0), blue.Support);

        Assert.Equal(7, SynergyCalculator.Score(withoutDraven));
    }

    [Fact]
    public void Frontline_ve_engage_yoksa_ve_hasar_tek_tipse_ceza_alir()
    {
        var allAd = new TeamDraft(
            Champion("a"), Champion("b"), Champion("c"), Champion("d"), Champion("e"));

        // frontline yok −4, engage yok 0, AP payı 0 → −3
        Assert.Equal(-7, SynergyCalculator.Score(allAd));
    }

    private static ChampionProfile Champion(string id) =>
        new(id, id, 5, 5, 5, frontline: 0, engage: 0, apShare: 0);
}
