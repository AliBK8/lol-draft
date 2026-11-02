using LolDraft.Engine.Simulation;
using static LolDraft.Engine.AttackRange;
using static LolDraft.Engine.ChampionClass;

namespace LolDraft.Engine.Tests;

/// <summary>Takım kuralları K5-K11, K13 (DESIGN.md §16.8).</summary>
public sealed class TeamRulesTests
{
    private static readonly IReadOnlyList<ChampionProfile> Early = TestTeams.EarlyBlue().Champions;
    private static readonly IReadOnlyList<ChampionProfile> Scale = TestTeams.ScaleRed().Champions;

    private static readonly IReadOnlyList<ChampionProfile> HardEngage =
        Team(Tags(engage: 3), Tags(engage: 3), Tags(engage: 3), Tags(), Tags(engage: 3));
    private static readonly IReadOnlyList<ChampionProfile> NoTags = Team(Tags(), Tags(), Tags(), Tags(), Tags());

    // K5: etkin engage = en − rakibin dis + peel'i. 12 vs 0 → tavan 4; sadece objektif ve son savaşta.
    [Theory]
    [InlineData("Objective", 4.0)]
    [InlineData("Final", 4.0)]
    [InlineData("Siege", 0.0)]
    public void Engage_objektif_ve_son_savasta_isler(string kind, double expected)
    {
        Assert.Equal(expected, Sum(Enum.Parse<FightKind>(kind), HardEngage, NoTags, 20, "K5"), 9);
    }

    [Fact]
    public void Engage_farki_tavanli_ve_disengage_ile_silinir()
    {
        IReadOnlyList<ChampionProfile> someEngage = Team(Tags(engage: 2), Tags(engage: 2), Tags(), Tags(), Tags());
        IReadOnlyList<ChampionProfile> somePeel = Team(Tags(peel: 2), Tags(), Tags(), Tags(), Tags());
        IReadOnlyList<ChampionProfile> disengage = Team(Tags(disengage: 3), Tags(peel: 3), Tags(disengage: 3), Tags(), Tags(peel: 3));

        Assert.Equal(4.0, Sum(FightKind.Final, someEngage, NoTags, 20, "K5"), 9);      // 4
        Assert.Equal(2.0, Sum(FightKind.Final, someEngage, somePeel, 20, "K5"), 9);    // 4 − 2
        Assert.Equal(0.0, Sum(FightKind.Final, someEngage, disengage, 20, "K5"), 9);   // 4 − 12 → 0
    }

    // Test takımları: erken comp en 7 − scale dis + peel 7 = 0; scale en 3 − erken dis + peel 3 = 0. Lulu'lu koruma
    // erken comp'un engage'ini tamamen siler (faz D: dis/peel engage'i tam ağırlıkla siler).
    [Fact]
    public void Koruma_engagei_siler()
    {
        Assert.DoesNotContain(TeamRules.Effects(FightKind.Final, Early, Scale, 20, _ => 5), e => e.Rule == "K5");
    }

    // K6: kuşatmada poke farkı (erken 2, scale 4 → Kırmızı +2). Objektifte erken comp en 7 < 8 (sert değil) → poke'u
    // fazla olan scale objektifi kurar: 0,5 × 2 = +1. Sert engage (en 12) karşısında poke'u fazla olan −2.
    [Fact]
    public void Poke_kusatmada_ve_objektifte_kazandirir_sert_engage_karsisinda_kaybettirir()
    {
        IReadOnlyList<ChampionProfile> poke = Team(Tags(poke: 3), Tags(poke: 3), Tags(), Tags(), Tags());

        Assert.Equal(-2.0, Sum(FightKind.Siege, Early, Scale, 20, "K6"), 9);
        Assert.Equal(-1.0, Sum(FightKind.Objective, Early, Scale, 20, "K6"), 9);
        Assert.Equal(0.0, Sum(FightKind.Final, Early, Scale, 20, "K6"), 9);
        Assert.Equal(2.0, Sum(FightKind.Objective, HardEngage, poke, 20, "K6"), 9);   // poke Kırmızı −2
        Assert.Equal(-2.0, Sum(FightKind.Objective, NoTags, poke, 20, "K6"), 9);      // poke Kırmızı +min(3, 2)
    }

    // K7: çatışmaya gelenlerin pick farkı. Lee Sin (3) + Leona (2) vs Sejuani (2) → Mavi +3 (tavan).
    [Fact]
    public void Pick_catismada_gelenlere_bakar()
    {
        Assert.Equal(1.0, Sum(FightKind.Skirmish, [Early[1]], [Scale[1]], 10, "K7"), 9);
        Assert.Equal(3.0, Sum(FightKind.Skirmish, [Early[1], Early[4]], [Scale[1]], 10, "K7"), 9);
        Assert.DoesNotContain(TeamRules.Effects(FightKind.Skirmish, Early, Scale, 10, _ => 5), e => e.Rule != "K7");
    }

    [Theory]
    [InlineData(new[] { "leesin" }, "kayle", 0.10)]               // pick 3 − mob 1
    [InlineData(new[] { "leesin", "leona" }, "kayle", 0.15)]      // 5 − 1 → tavan
    [InlineData(new[] { "sejuani" }, "leblanc", -0.05)]          // 2 − 3
    [InlineData(new[] { "sejuani", "lulu" }, "leblanc", -0.05)]  // Lulu pick 0
    public void Gank_basarisi_pick_ve_mobla_degisir(string[] gankers, string victim, double expected)
    {
        Assert.Equal(expected, TeamRules.GankPickBonus(gankers.Select(ById), ById(victim)), 9);
    }

    // K8: rakipte hiper taşıyıcı ve rakip peel + fl < 3 → 0,8 × dive (tavan 3).
    [Fact]
    public void Dive_korumasiz_hiper_tasiyiciyi_cezalandirir()
    {
        var jinx = Scale[3];
        IReadOnlyList<ChampionProfile> unprotected = Team(Tags(frontline: 1), Tags(), Tags(), jinx, Tags(peel: 1));

        Assert.Equal(3.0, Sum(FightKind.Final, Early, unprotected, 20, "K8"), 9);   // dive 7 × 0,8 → tavan 3
        Assert.Equal(0.0, Sum(FightKind.Final, Early, Scale, 20, "K8"), 9);         // scale peel + fl 8
    }

    // K9: hiper taşıyıcılı takım dk ≥ 25'te peel ≥ 3 → +2; peel + fl < 2 → taşıyıcı gücü × 0,85.
    [Fact]
    public void Korunan_tasiyici_gec_oyunda_guclenir_korumasiz_zayiflar()
    {
        Assert.Equal(-2.0, Sum(FightKind.Final, Early, Scale, 25, "K9"), 9);   // Kırmızı +2
        Assert.Equal(0.0, Sum(FightKind.Final, Early, Scale, 24, "K9"), 9);

        var jinx = Scale[3];
        IReadOnlyList<ChampionProfile> unprotected = Team(Tags(frontline: 1), Tags(), Tags(), jinx, Tags());
        Assert.Equal(-0.15 * 10, Sum(FightKind.Final, unprotected, Early, 30, "K9", power: 10), 9);
    }

    // K10: fl toplamı < 2 → −3; rakip en ≥ 4 ise −4. K11: AP payı < %20 ya da > %80 → −2.
    [Fact]
    public void Frontline_eksigi_ve_tek_tip_hasar_cezalandirilir()
    {
        IReadOnlyList<ChampionProfile> noFrontline = Team(Tags(frontline: 1), Tags(), Tags(), Tags(), Tags());

        Assert.Equal(-4.0, Sum(FightKind.Final, noFrontline, Early, 20, "K10"), 9);   // erken comp en 7
        Assert.Equal(-3.0, Sum(FightKind.Final, noFrontline, Scale, 20, "K10"), 9);   // scale comp en 3
        Assert.Equal(-2.0, Sum(FightKind.Final, noFrontline, Early, 20, "K11"), 9);   // hepsi AD
        Assert.Equal(0.0, Sum(FightKind.Final, Early, Scale, 20, "K10") + Sum(FightKind.Final, Early, Scale, 20, "K11"), 9);
    }

    // K13: Leona + Draven (Mavi), Kayle + Lulu ve Jinx + Lulu (Kırmızı): her kombo +2.
    [Fact]
    public void Kombo_iki_sampiyon_birlikteyse_sayilir()
    {
        var effects = TeamRules.Effects(FightKind.Final, Early, Scale, 20, _ => 5).Where(e => e.Rule == "K13").ToList();
        Assert.Equal(new[] { TeamSide.Blue, TeamSide.Red, TeamSide.Red }, effects.Select(e => e.Side));
        Assert.Equal(-2.0, effects.Sum(e => e.Signed), 9);
    }

    // Test takımlarında son savaş, dk 30: K5 yok (koruma siler), K9 Kırmızı +2, K13 Mavi +2 / Kırmızı +4 → Kırmızı +4.
    // 20. dk objektif: K6 Kırmızı +1 (poke objektifi kurdu), K13 net Kırmızı +2 → Kırmızı +3.
    // Erken comp'un kuralı yok: avantajı güç eğrisinde (erken 9'lar) ve altında; scale comp'unki kurallarda.
    [Fact]
    public void Scale_comp_kurallarla_erken_comp_guc_egrisiyle_ustun()
    {
        Assert.Equal(-4.0, Sum(FightKind.Final, Early, Scale, 30), 9);
        Assert.Equal(-3.0, Sum(FightKind.Objective, Early, Scale, 20), 9);
    }

    private static double Sum(
        FightKind kind, IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, int minute, string? rule = null,
        double power = 5) =>
        TeamRules.Effects(kind, blue, red, minute, _ => power).Where(e => rule is null || e.Rule == rule).Sum(e => e.Signed);

    private static ChampionProfile ById(string id) => Early.Concat(Scale).Single(c => c.Id == id);

    private static ChampionTags Tags(int frontline = 0, int engage = 0, int disengage = 0, int peel = 0, int poke = 0) =>
        new(frontline, engage, disengage, peel, poke);

    private static int _next;

    // Takım: ChampionTags verilenler AD dövüşçü (hiper taşıyıcı değil), ChampionProfile verilenler olduğu gibi.
    private static IReadOnlyList<ChampionProfile> Team(params object[] slots) =>
        slots.Select(s => s as ChampionProfile
                          ?? new ChampionProfile($"t{Interlocked.Increment(ref _next)}", "T", Melee, Fighter, null, 5, 5, 5,
                              economy: 2, apShare: 0, (ChampionTags)s))
            .ToList();
}
