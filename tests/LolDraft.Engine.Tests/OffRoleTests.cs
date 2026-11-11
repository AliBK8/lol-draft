using LolDraft.Engine.Simulation;
using static LolDraft.Engine.AttackRange;
using static LolDraft.Engine.ChampionClass;

namespace LolDraft.Engine.Tests;

/// <summary>Rol dışı oynama: ortalama ceza + lane fazında oynaklık + karne (DESIGN.md §16.8).</summary>
public sealed class OffRoleTests
{
    private const int MatchCount = 400;

    private readonly IMatchSimulator _simulator = MatchEngine.CreateSimulator();

    [Theory]
    [InlineData(ChampionRole.Top, ChampionRole.Top, 0.0)]
    [InlineData(ChampionRole.Top, ChampionRole.Mid, 0.20)]      // yakın
    [InlineData(ChampionRole.Mid, ChampionRole.Jungle, 0.25)]   // orman
    [InlineData(ChampionRole.Support, ChampionRole.Bot, 0.30)]
    [InlineData(ChampionRole.Support, ChampionRole.Top, 0.35)]  // uzak
    [InlineData(ChampionRole.Bot, ChampionRole.Jungle, 0.35)]
    public void Ceza_rol_mesafesine_gore(ChampionRole natural, ChampionRole played, double penalty) =>
        Assert.Equal(penalty, OffRole.Penalty(Champion("x", natural), played));

    [Fact]
    public void Cok_rollu_sampiyon_en_yakin_rolunu_sayar()
    {
        var mage = Champion("lux", ChampionRole.Mid, ChampionRole.Support);

        Assert.Equal(0.20, OffRole.Penalty(mage, ChampionRole.Bot));   // mid→bot, sup→bot değil
        Assert.Equal(0.0, OffRole.Penalty(mage, ChampionRole.Support));
    }

    [Fact]
    public void Rol_bilgisi_olmayan_her_rolde_cezasiz() =>
        Assert.Equal(0.0, OffRole.Penalty(Champion("x"), ChampionRole.Support));

    [Fact]
    public void Rol_dahilinde_karne_bos_rol_disinda_dolu()
    {
        var inRole = _simulator.Simulate(EvenWithBlueTop(ChampionRole.Top, seed: 3));
        var offRole = _simulator.Simulate(EvenWithBlueTop(ChampionRole.Mid, seed: 3));

        Assert.Empty(inRole.Report.OffRoles);
        var report = Assert.Single(offRole.Report.OffRoles);
        Assert.Equal(("b0", TeamSide.Blue, ChampionRole.Top, 0.20), (report.ChampionId, report.Side, report.Role, report.Penalty));
        Assert.StartsWith("Rol dışı", report.Text);
        Assert.Contains("top'ta", report.Text);
        Assert.EndsWith("(−%20)", report.Text);
    }

    // Kumar: rol dışı top ortalamada koridorunu kaybeder ama sonuçları daha dağınıktır (ezer ya da ezilir).
    // Koridor puanı (Breakdown.Top) lane fazındaki Δ'ların toplamı: ortalaması düşer, yayılımı artar.
    [Fact]
    public void Rol_disi_koridor_ortalamada_geride_ama_daha_oynak()
    {
        var inRole = TopLaneScores(ChampionRole.Top);
        var offRole = TopLaneScores(ChampionRole.Mid);

        Assert.True(offRole.Average() < inRole.Average() - 1, $"ortalama {inRole.Average():F2} → {offRole.Average():F2}");
        Assert.True(StdDev(offRole) > StdDev(inRole) * 1.2, $"yayılım {StdDev(inRole):F2} → {StdDev(offRole):F2}");
    }

    [Fact]
    public void Rol_disi_oynayan_takim_daha_az_kazanir_ama_hala_kazanabilir()
    {
        var wins = Enumerable.Range(0, MatchCount)
            .Count(seed => _simulator.Simulate(EvenWithBlueTop(ChampionRole.Support, (uint)seed)).End.Winner == TeamSide.Blue);
        var rate = wins / (double)MatchCount;

        Assert.InRange(rate, 0.15, 0.48);   // %35 ceza: geride ama şansı var
    }

    private List<double> TopLaneScores(ChampionRole blueTopNatural) =>
    [
        .. Enumerable.Range(0, MatchCount)
            .Select(seed => _simulator.Simulate(EvenWithBlueTop(blueTopNatural, (uint)seed)).Breakdown[ScoreCategory.Top]),
    ];

    private static double StdDev(List<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }

    // Eşit draft (TestTeams.Even), ama roller dolu: herkes kendi yuvasında, sadece Mavi top'un doğal rolü verilen.
    private static MatchInput EvenWithBlueTop(ChampionRole blueTopNatural, uint seed)
    {
        var even = TestTeams.Even(seed);
        return new MatchInput(WithRoles(even.Blue, blueTopNatural), WithRoles(even.Red, ChampionRole.Top), even.Seed);
    }

    private static TeamDraft WithRoles(TeamDraft team, ChampionRole topNatural) => new(
        WithRole(team.Top, topNatural), WithRole(team.Jungle, ChampionRole.Jungle), WithRole(team.Mid, ChampionRole.Mid),
        WithRole(team.Bot, ChampionRole.Bot), WithRole(team.Support, ChampionRole.Support));

    private static ChampionProfile WithRole(ChampionProfile c, ChampionRole role) =>
        new(c.Id, c.Name, c.Range, c.PrimaryClass, c.SecondaryClass, c.Early, c.Mid, c.Late, c.Economy, c.ApShare, c.Tags, [role]);

    private static ChampionProfile Champion(string id, params ChampionRole[] roles) =>
        new(id, id, Melee, Fighter, null, 6, 6, 6, economy: 2, apShare: 0, new ChampionTags(), roles);
}
