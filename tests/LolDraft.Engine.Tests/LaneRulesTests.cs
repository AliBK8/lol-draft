using LolDraft.Engine.Simulation;
using static LolDraft.Engine.AttackRange;
using static LolDraft.Engine.ChampionClass;

namespace LolDraft.Engine.Tests;

/// <summary>Koridor kuralları K1-K4 (DESIGN.md §16.8).</summary>
public sealed class LaneRulesTests
{
    private static readonly ChampionProfile Jayce = Champ("jayce", Ranged, Fighter, Mage, new ChampionTags(poke: 3, mobility: 2));
    private static readonly ChampionProfile Melee0 = Champ("melee", Melee, Fighter, null, new ChampionTags());
    private static readonly ChampionProfile Renekton = TestTeams.EarlyBlue().Top;   // melee, mob 2
    private static readonly ChampionProfile TankTop = Champ("tank", Melee, Tank, null, new ChampionTags(frontline: 3, engage: 2, mobility: 1));
    private static readonly ChampionProfile Zed = Champ("zed", Melee, Assassin, null, new ChampionTags(pick: 2, dive: 3, mobility: 3));
    private static readonly ChampionProfile Orianna = Champ("orianna", Ranged, Mage, null, new ChampionTags(engage: 1, poke: 2));
    private static readonly ChampionProfile LeBlanc = TestTeams.EarlyBlue().Mid;
    private static readonly ChampionProfile Kassadin = TestTeams.ScaleRed().Mid;

    // K1
    [Theory]
    [InlineData(5, 0.3)]     // melee'nin mesafe kapatacak bir şeyi yok
    [InlineData(13, 0.3)]
    [InlineData(14, 0.0)]    // lane fazı bitti
    public void Range_melee_karsisinda_baski_kurar(int minute, double expected)
    {
        Assert.Equal(expected, Sum(Lane.Top, [Jayce], [Melee0], minute), 9);
        Assert.Equal(-expected, Sum(Lane.Top, [Melee0], [Jayce], minute), 9);
    }

    [Fact]
    public void Hareketli_ya_da_engageli_melee_farki_kapatir()
    {
        Assert.Equal(0.1, Sum(Lane.Top, [Jayce], [Renekton], 5), 9);   // mob 2
        Assert.Equal(0.1, Sum(Lane.Top, [Jayce], [TankTop], 5), 9);    // en 2
    }

    [Fact]
    public void Range_kurali_ayni_menzilde_ve_botta_islemez()
    {
        Assert.Empty(LaneRules.Effects(Lane.Top, [Renekton], [Melee0], 5));
        Assert.Empty(LaneRules.Effects(Lane.Top, [Jayce], [Orianna], 5));

        // Bot'ta melee Leona vs ranged Lulu: K1 yok (bot'u K4 karşılar; Lulu'nun korumasıyla K4 de yok).
        var blue = TestTeams.EarlyBlue();
        var red = TestTeams.ScaleRed();
        Assert.Empty(LaneRules.Effects(Lane.Bot, [blue.Bot, blue.Support], [red.Bot, red.Support], 5));
    }

    // K2
    [Fact]
    public void Tank_koridoru_lane_fazinda_yumusatir()
    {
        Assert.Equal(0.7, LaneRules.Damping(Lane.Top, [TankTop], [Jayce], 5).Factor, 9);
        Assert.Equal(0.7, LaneRules.Damping(Lane.Top, [Jayce], [TankTop], 5).Factor, 9);
        Assert.Equal(0.7, LaneRules.Damping(Lane.Top, [TankTop], [TankTop with { }], 5).Factor, 9);   // bir kez
        Assert.Equal(1.0, LaneRules.Damping(Lane.Top, [TankTop], [Jayce], 14).Factor, 9);
        Assert.Equal(1.0, LaneRules.Damping(Lane.Top, [Renekton], [Jayce], 5).Factor, 9);

        var blue = TestTeams.EarlyBlue();   // bot'ta Leona tank ama tampon değil: bot'u K4 karşılar
        var red = TestTeams.ScaleRed();
        Assert.Equal(1.0, LaneRules.Damping(Lane.Bot, [blue.Bot, blue.Support], [red.Bot, red.Support], 5).Factor, 9);
    }

    // K3
    [Theory]
    [InlineData(5, 0.3)]     // erken all-in: suikastçı
    [InlineData(12, 0.0)]    // ara
    [InlineData(18, -0.2)]   // dalga temizliği: büyücü
    [InlineData(30, -0.2)]
    public void Suikastci_erken_buyucu_gec_ustun(int minute, double expected)
    {
        Assert.Equal(expected, Sum(Lane.Mid, [Zed], [Orianna], minute, "K3"), 9);
    }

    [Fact]
    public void Kurallar_toplanir()
    {
        // Zed vs Orianna, 5. dk: K3 Zed +0,3, K1 Orianna (range, ama Zed mob 3 → mesafeyi kapatır) +0,1 → net +0,2.
        Assert.Equal(0.2, Sum(Lane.Mid, [Zed], [Orianna], 5), 9);
    }

    [Fact]
    public void Suikastci_kurali_sadece_ortada()
    {
        Assert.DoesNotContain(LaneRules.Effects(Lane.Top, [Zed], [Orianna], 5), e => e.Rule == "K3");
    }

    [Fact]
    public void Iki_taraf_da_suikastci_buyucuyse_birbirini_goturur()
    {
        // LeBlanc (ranged) vs Kassadin (melee, mob 3): K3 iki yöne de tutar, sıfırlanır; kalan K1 +0,1.
        var effects = LaneRules.Effects(Lane.Mid, [LeBlanc], [Kassadin], 5);
        Assert.Equal(0.0, effects.Where(e => e.Rule == "K3").Sum(e => e.Signed), 9);
        Assert.Equal(0.1, effects.Sum(e => e.Signed), 9);
    }

    // K4
    [Fact]
    public void Engage_destek_korumasiz_bota_baski_kurar()
    {
        var blue = TestTeams.EarlyBlue();
        var red = TestTeams.ScaleRed();
        var mageSupport = Champ("brand", Ranged, Mage, null, new ChampionTags(disengage: 1, poke: 3, pick: 1));

        // Leona vs Jinx + Büyücü Destek: peel + dis = 1 < 2 → +0,25
        Assert.Equal(0.25, Sum(Lane.Bot, [blue.Bot, blue.Support], [red.Bot, mageSupport], 5), 9);
        // Leona vs Jinx + Lulu: peel 3 + dis 2 → yok
        Assert.Equal(0.0, Sum(Lane.Bot, [blue.Bot, blue.Support], [red.Bot, red.Support], 5), 9);
        // Lane fazı bitti
        Assert.Equal(0.0, Sum(Lane.Bot, [blue.Bot, blue.Support], [red.Bot, mageSupport], 14), 9);
    }

    [Fact]
    public void Etki_kuralin_kaynagini_tasir()
    {
        var effect = Assert.Single(LaneRules.Effects(Lane.Top, [Melee0], [Jayce], 5));
        Assert.Equal(("K1", TeamSide.Red, Jayce), (effect.Rule, effect.Side, effect.Source));
    }

    // Motorda: eşit draft'ta Mavi üstü range'e çevirmek (karşısı mesafe kapatamayan melee) 14. dk'da CS farkına döner.
    // Karşılaştırma: aynı draft, Mavi üst melee. Sinerji iki durumda aynı (engage yok, frontline var).
    [Fact]
    public void Range_ustunlugu_macta_cs_farkina_doner()
    {
        const int matches = 500;
        var simulator = MatchEngine.CreateSimulator();
        var (withRule, without) = (0.0, 0.0);
        for (var seed = 0u; seed < matches; seed++)
        {
            var even = TestTeams.Even(seed);
            var red = new TeamDraft(Champ("r0", Melee, Fighter, null, new ChampionTags(frontline: 2), power: 6),
                even.Red.Jungle, even.Red.Mid, even.Red.Bot, even.Red.Support);

            withRule += TopCsGap(simulator.Simulate(new MatchInput(WithTop(even.Blue, Ranged), red, even.Seed)));
            without += TopCsGap(simulator.Simulate(new MatchInput(WithTop(even.Blue, Melee), red, even.Seed)));
        }

        Assert.True(withRule / matches > 5, $"Range'li üst ortalama CS farkı {withRule / matches:F1}");
        Assert.True(Math.Abs(without / matches) < 3, $"Melee üst ortalama CS farkı {without / matches:F1}");

        static TeamDraft WithTop(TeamDraft team, AttackRange range) =>
            new(Champ("b0", range, Fighter, null, new ChampionTags(frontline: 2), power: 6),
                team.Jungle, team.Mid, team.Bot, team.Support);
    }

    private static double TopCsGap(MatchResult result)
    {
        var frame = result.GoldFrames.First(f => f.Minute == 14);
        return frame.Champions[0].Cs - frame.Champions[5].Cs;
    }

    private static double Sum(Lane lane, ChampionProfile[] blue, ChampionProfile[] red, int minute, string? rule = null) =>
        LaneRules.Effects(lane, blue, red, minute).Where(e => rule is null || e.Rule == rule).Sum(e => e.Signed);

    private static ChampionProfile Champ(
        string id, AttackRange range, ChampionClass primary, ChampionClass? secondary, ChampionTags tags, int power = 5) =>
        new(id, id, range, primary, secondary, power, power, power, economy: 2, apShare: 0, tags);
}
