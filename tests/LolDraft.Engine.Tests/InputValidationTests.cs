namespace LolDraft.Engine.Tests;

public sealed class InputValidationTests
{
    [Theory]
    [InlineData(0, 5, 5, 0, 0, 0.0)]
    [InlineData(11, 5, 5, 0, 0, 0.0)]
    [InlineData(5, 5, 5, 4, 0, 0.0)]
    [InlineData(5, 5, 5, 0, -1, 0.0)]
    [InlineData(5, 5, 5, 0, 0, 1.5)]
    [InlineData(5, 5, 5, 0, 0, double.NaN)]
    public void Aralik_disi_sampiyon_olusturulamaz(int early, int mid, int late, int frontline, int engage, double ap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ChampionProfile("x", "X", early, mid, late, frontline, engage, ap));
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
}
