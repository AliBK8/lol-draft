namespace LolDraft.Engine.Simulation;

/// <summary>
/// Bir kuralın bir andaki etkisi: hangi kural, kimin lehine, ne kadar, hangi şampiyon yüzünden ve karnedeki metni
/// ("Range üstünlüğü (Jayce)").
/// </summary>
internal sealed record RuleEffect(string Rule, TeamSide Side, double Value, ChampionProfile Source, string Text)
{
    /// <summary>Mavi'nin gözünden: Mavi lehine pozitif.</summary>
    public double Signed => Side.Sign() * Value;
}

/// <summary>
/// Koridor kuralları K1-K4 (DESIGN.md §16.8): draft'tan gelen koridor avantajı, Δ'ya eklenir.
/// Saf: sadece şampiyonlara ve dakikaya bakar, zar atmaz.
/// </summary>
internal static class LaneRules
{
    // Lane fazı: kurallar aksi belirtilmedikçe dk < 14.
    private const int LanePhaseEnd = 14;

    // K1 Range baskısı (üst, orta): ranged vs melee → ranged'e +0,3/dk; melee'nin max(mob, en) ≥ 2 ise aradaki
    // mesafeyi kapatır → +0,1. Örnek: Jayce vs Renekton (mob 2) +0,1 · Jayce vs Tank Top (mob 1, en 2) +0,1.
    private const double RangeBonus = 0.3;
    private const double RangeBonusVsGapCloser = 0.1;
    private const int GapCloserThreshold = 2;

    // K2 Tank tamponu (üst, orta): koridorda tank (ana sınıf) varsa Δ × 0,7; iki tarafta da olsa bir kez.
    private const double TankDamping = 0.7;

    // K3 Suikastçı all-in (orta): suikastçı (mob ≥ 2) vs büyücü/nişancı.
    // dk < 10: suikastçıya +0,3 (erken all-in) · dk ≥ 18: büyücüye +0,2 (dalga temizliği, menzil).
    private const int AllInEnd = 10;
    private const int WaveclearStart = 18;
    private const double AllInBonus = 0.3;
    private const double WaveclearBonus = 0.2;
    private const int AssassinMinMobility = 2;

    // K4 Bot engage: destek en ≥ 2 ve rakip bot'un (adc + destek) peel + dis toplamı < 2 → bot'a +0,25/dk.
    // Örnek: Leona vs Draven + Büyücü Destek (dis 1) → +0,25 · Leona vs Jinx + Lulu (peel 3, dis 2) → yok.
    private const int EngageThreshold = 2;
    private const int ProtectionThreshold = 2;
    private const double BotEngageBonus = 0.25;

    /// <summary>
    /// Koridordaki şampiyonlar: üst/orta tek kişi, bot (adc, destek). Etkiler kural sırasıyla (K1, K3, K4).
    /// </summary>
    public static IReadOnlyList<RuleEffect> Effects(
        Lane lane, IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, int minute)
    {
        var effects = new List<RuleEffect>();
        if (lane is Lane.Top or Lane.Mid)
        {
            if (minute < LanePhaseEnd)
            {
                RangePressure(TeamSide.Blue, blue[0], red[0], effects);
                RangePressure(TeamSide.Red, red[0], blue[0], effects);
            }

            if (lane == Lane.Mid)
            {
                AssassinAllIn(TeamSide.Blue, blue[0], red[0], minute, effects);
                AssassinAllIn(TeamSide.Red, red[0], blue[0], minute, effects);
            }
        }
        else if (minute < LanePhaseEnd)
        {
            BotEngage(TeamSide.Blue, blue[1], red, effects);
            BotEngage(TeamSide.Red, red[1], blue, effects);
        }

        return effects;
    }

    /// <summary>
    /// K2: koridor Δ'sının çarpanı (1 = etkisiz). Etki karne için: tamponu sağlayan tank ve tarafı (değeri 0; etkisi
    /// çarpan, toplanmaz).
    /// </summary>
    public static (double Factor, RuleEffect? Effect) Damping(
        Lane lane, IReadOnlyList<ChampionProfile> blue, IReadOnlyList<ChampionProfile> red, int minute)
    {
        if (lane == Lane.Bot || minute >= LanePhaseEnd)
            return (1, null);

        var tank = new[] { blue[0], red[0] }.FirstOrDefault(c => c.PrimaryClass == ChampionClass.Tank);
        if (tank is null)
            return (1, null);

        var side = tank == blue[0] ? TeamSide.Blue : TeamSide.Red;
        return (TankDamping, new RuleEffect("K2", side, 0, tank, $"Tank koridoru sakinleştirdi ({tank.Name})"));
    }

    private static void RangePressure(TeamSide side, ChampionProfile ranged, ChampionProfile melee, List<RuleEffect> effects)
    {
        if (ranged.Range != AttackRange.Ranged || melee.Range != AttackRange.Melee)
            return;

        var closesGap = Math.Max(melee.Tags.Mobility, melee.Tags.Engage) >= GapCloserThreshold;
        effects.Add(new RuleEffect("K1", side, closesGap ? RangeBonusVsGapCloser : RangeBonus, ranged,
            $"Range üstünlüğü ({ranged.Name})"));
    }

    private static void AssassinAllIn(
        TeamSide side, ChampionProfile assassin, ChampionProfile target, int minute, List<RuleEffect> effects)
    {
        var isAssassin = assassin.HasClass(ChampionClass.Assassin) && assassin.Tags.Mobility >= AssassinMinMobility;
        var isTarget = target.HasClass(ChampionClass.Mage) || target.HasClass(ChampionClass.Marksman);
        if (!isAssassin || !isTarget)
            return;

        if (minute < AllInEnd)
            effects.Add(new RuleEffect("K3", side, AllInBonus, assassin, $"Erken all-in ({assassin.Name})"));
        else if (minute >= WaveclearStart)
            effects.Add(new RuleEffect("K3", side.Opponent(), WaveclearBonus, target, $"Dalga temizliği ({target.Name})"));
    }

    private static void BotEngage(
        TeamSide side, ChampionProfile support, IReadOnlyList<ChampionProfile> rivals, List<RuleEffect> effects)
    {
        var protection = rivals.Sum(c => c.Tags.Peel + c.Tags.Disengage);
        if (support.Tags.Engage >= EngageThreshold && protection < ProtectionThreshold)
            effects.Add(new RuleEffect("K4", side, BotEngageBonus, support, $"Bot'ta engage baskısı ({support.Name})"));
    }
}
