namespace LolDraft.Engine.Tests;

public sealed class InputValidationTests
{
    [Theory]
    [InlineData(0, 5, 5, 2, 0.0)]
    [InlineData(11, 5, 5, 2, 0.0)]
    [InlineData(5, 5, 5, 4, 0.0)]
    [InlineData(5, 5, 5, -1, 0.0)]
    [InlineData(5, 5, 5, 2, 1.5)]
    [InlineData(5, 5, 5, 2, double.NaN)]
    public void Aralik_disi_sampiyon_olusturulamaz(int early, int mid, int late, int economy, double ap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Champion(early: early, mid: mid, late: late, economy: economy, ap: ap));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, -1)]
    public void Aralik_disi_etiket_olusturulamaz(int frontline, int mobility)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChampionTags(frontline: frontline, mobility: mobility));
    }

    [Fact]
    public void Ikincil_sinif_ana_sinifla_ayni_olamaz()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Champion(secondary: ChampionClass.Fighter));
    }

    [Fact]
    public void Bilinmeyen_sinif_ve_menzil_olusturulamaz()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Champion(primary: (ChampionClass)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Champion(range: (AttackRange)99));
    }

    [Fact]
    public void Ayni_sampiyon_takimda_iki_kez_olamaz()
    {
        var blue = TestTeams.EarlyBlue();

        Assert.Throws<ArgumentException>(() =>
            new TeamDraft(blue.Top, blue.Jungle, blue.Mid, blue.Bot, blue.Top));
    }

    [Fact]
    public void Ayni_sampiyon_iki_takimda_olamaz()
    {
        Assert.Throws<ArgumentException>(() =>
            new MatchInput(TestTeams.EarlyBlue(), TestTeams.EarlyBlue(), new Seed(1)));
    }

    [Fact]
    public void Ayni_icerikli_draftlar_esittir()
    {
        Assert.Equal(TestTeams.EarlyBlue(), TestTeams.EarlyBlue());
    }

    private static ChampionProfile Champion(
        AttackRange range = AttackRange.Melee, ChampionClass primary = ChampionClass.Fighter, ChampionClass? secondary = null,
        int early = 5, int mid = 5, int late = 5, int economy = 2, double ap = 0) =>
        new("x", "X", range, primary, secondary, early, mid, late, economy, ap, new ChampionTags());
}
