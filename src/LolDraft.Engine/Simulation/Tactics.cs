namespace LolDraft.Engine.Simulation;

/// <summary>
/// Oyun planlarının ayar düğmeleri (DESIGN.md §4.10). Her plan motorun bazı sabitlerini o takım için çevirir.
/// Uyum, planı ne kadar iyi uyguladığın: uyan draft kazancı ×1,25 alır, bedeli ×0,75 öder; uymayan kazancı ×0,75,
/// bedeli ×1,25 (nötr ikisi de ×1). Dengeli bütün düğmeleri sıfır bırakır: motor plansızla bit bit aynı oynar
/// (BalancedPlanFingerprintTests).
/// Ayar burada: bir planın gücünü değiştirmek bu tablodaki sayıyı değiştirmek (sonra denge aracı, --plans).
/// </summary>
internal sealed record PlanKnobs
{
    // ---- Kazançlar (uyum çarpanıyla ölçeklenir) ----

    /// <summary>Ejder ve Elder savaşında d +.</summary>
    public double DragonFightBonus { get; init; }

    /// <summary>LatePowerFrom dakikasından sonra güç × (1 + bu).</summary>
    public double LatePowerGain { get; init; }

    public int LatePowerFrom { get; init; }

    /// <summary>Ek gank pencerelerinin (dk 8 ve 11) her birinde gank ihtimali; gank'ı bu takım atar.</summary>
    public double ExtraGankChance { get; init; }

    /// <summary>EarlySkirmishUntil dakikasından önce nehir çatışmasında d +.</summary>
    public double EarlySkirmishBonus { get; init; }

    public int EarlySkirmishUntil { get; init; }

    /// <summary>Bu takımın gank'ının bot'a gitme ihtimali (0 = normal, üç koridor eşit).</summary>
    public double BotGankShare { get; init; }

    /// <summary>Bot'a gank başarısına +.</summary>
    public double BotGankSuccessBonus { get; init; }

    /// <summary>Split'e gitme ihtimaline +.</summary>
    public double SplitChanceBonus { get; init; }

    /// <summary>Rakibin split'e cevap verme ihtimalinden − (plansız %50).</summary>
    public double AnswerChanceCut { get; init; }

    /// <summary>Split düellosunda splitter'a d +.</summary>
    public double DuelBonus { get; init; }

    /// <summary>LaneGainUntil dakikasından önce her koridorda akış (Δ) +.</summary>
    public double LaneGain { get; init; }

    public int LaneGainUntil { get; init; }

    /// <summary>BotGainUntil dakikasından önce bot koridor akışı (Δ) +.</summary>
    public double BotLaneGain { get; init; }

    public int BotGainUntil { get; init; }

    /// <summary>Kuşatma için gereken altın farkı × (1 − bu).</summary>
    public double SiegeLeadDiscount { get; init; }

    /// <summary>İstila çatışmasında d + (bu takım istila ediyorsa).</summary>
    public double InvadeFightBonus { get; init; }

    /// <summary>Harita düzeninde (DESIGN.md §4.11) 1-4 ve 1-3-1 ağırlığı × (1 + bu).</summary>
    public double SplitLayoutTilt { get; init; }

    /// <summary>Harita düzeninde Grup ağırlığı × (1 + bu).</summary>
    public double GroupLayoutTilt { get; init; }

    // ---- Ayrık kazançlar (ölçeklenmez: açık ya da kapalı) ----

    /// <summary>Split etiketi 1 olan da split'e gidebilir (normalde ≥ 2).</summary>
    public bool SplitAnyTag { get; init; }

    /// <summary>Kuşatmalar arası bekleme bu kadar dakika kısa.</summary>
    public int SiegeCooldownCut { get; init; }

    /// <summary>4. dakikada istila kesin ve bu takımdan.</summary>
    public bool ForceInvade { get; init; }

    // ---- Bedeller (uyumla ters ölçeklenir: uyan ×0,75, uymayan ×1,25) ----

    /// <summary>Herald savaşında d −.</summary>
    public double HeraldFightPenalty { get; init; }

    /// <summary>LanePenaltyUntil dakikasından önce her koridorda akış (Δ) −.</summary>
    public double LanePenalty { get; init; }

    public int LanePenaltyUntil { get; init; }

    /// <summary>TopPenaltyUntil dakikasından önce top koridor akışı (Δ) −.</summary>
    public double TopLanePenalty { get; init; }

    public int TopPenaltyUntil { get; init; }

    /// <summary>LatePowerLossFrom dakikasından sonra güç × (1 − bu).</summary>
    public double LatePowerLoss { get; init; }

    public int LatePowerLossFrom { get; init; }

    /// <summary>Objektif ve son savaşta d − (bir kişi geç gelir).</summary>
    public double TeamFightPenalty { get; init; }

    /// <summary>Bu takım kuşatırken savunanın ev sahibi avantajına +.</summary>
    public double SiegeDefenseExtra { get; init; }

    /// <summary>Bu takımın kuşatması tutulursa (throw) savunanın en fazla kill'ine + (ayrık, ölçeklenmez).</summary>
    public int ThrowExtraKills { get; init; }

    /// <summary>Bu takımın istilası püskürtülürse rakibin orman temposu bu kadar (normalde 1) ve bu kadar dakika.</summary>
    public double InvadeLossTempo { get; init; }

    public int InvadeLossTempoMinutes { get; init; }

    /// <summary>
    /// Planların tablosu (DENGE.md, oyun planı turları). Tur 1 → 2 (kullanıcı, 2026-11-24): kazanç ve bedeller ×1,5
    /// (plan seçimi hissedilsin, uyumun etkisi büyüsün); Split'in takım savaşı cezası kalktı (split olunca splitter
    /// zaten savaşta yok: bedel iki kez kesiliyordu, %45,6). Tur 2 → 3 (kullanıcı): uyumun hissedilmesi için her plana
    /// sürekli bir kazanç; Split sıklık değil etkinlik (split motorda zaten kaybettiren hamle: sıklaştıkça kaybettiriyordu).
    /// </summary>
    public static PlanKnobs Of(GamePlan plan) => plan switch
    {
        GamePlan.Balanced => Balanced,
        // Ejder & late: ejderlerde ve geç oyunda güçlü; Herald'ı ve erken koridorları verir.
        GamePlan.DragonLate => new()
        {
            DragonFightBonus = 4.5, LatePowerGain = 0.06, LatePowerFrom = 25,
            HeraldFightPenalty = 4.5, LanePenalty = 0.225, LanePenaltyUntil = 20,
        },
        // Erken baskı: 14. dk'ya kadar koridorlarda önde, 2 ek gank penceresi, erken çatışmada önde; 20. dk'dan sonra söner.
        GamePlan.EarlyPressure => new()
        {
            LaneGain = 0.15, LaneGainUntil = 14,
            ExtraGankChance = 1, EarlySkirmishBonus = 3, EarlySkirmishUntil = 15,
            LatePowerLoss = 0.06, LatePowerLossFrom = 20,
        },
        // Bot baskısı: lane fazında bot koridoru önde, gank'ların çoğu bot'a ve daha başarılı; top yalnız kalır.
        GamePlan.BotPressure => new()
        {
            BotLaneGain = 0.25, BotGainUntil = 14,
            BotGankShare = 0.73, BotGankSuccessBonus = 0.1,
            TopLanePenalty = 0.25, TopPenaltyUntil = 14,
        },
        // Split: etiketi 1 olan da gidebilir; split daha etkili: rakip daha az cevap verir (%50 → %35), splitter
        // düelloda +2. Bedeli kendinde: split olunca splitter objektif savaşında yok (4'e 5), ayrı ceza yok.
        GamePlan.Split => new()
        {
            SplitAnyTag = true, AnswerChanceCut = 0.15, DuelBonus = 2,
            SplitLayoutTilt = 1,
        },
        // Hızlı bitir: kuşatmaya daha az farkla ve daha sık gider; tutulursa daha pahalı.
        GamePlan.FastFinish => new()
        {
            SiegeLeadDiscount = 0.3, SiegeCooldownCut = 1,
            SiegeDefenseExtra = 3, ThrowExtraKills = 1,
            GroupLayoutTilt = 1,
        },
        // Ormancı istilası: istila kesin ve bu takımdan, çatışmada önde; püskürtülürse ormancı çok geride
        // (rakibin temposu 1 yerine 2,5).
        GamePlan.Invade => new()
        {
            ForceInvade = true, InvadeFightBonus = 3,
            InvadeLossTempo = 2.5, InvadeLossTempoMinutes = 4,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
    };

    private static readonly PlanKnobs Balanced = new();
}

/// <summary>Bir takımın planı, draft'a uyumu ve ölçeklenmiş düğmeleri. Dengeli'de bütün değerler 0 ya da 1.</summary>
internal sealed class TeamTactics
{
    // Uyum: uyan kazancı ×1,25 alır, bedeli ×0,75 öder; uymayan tersi.
    private const double GoodFitGain = 1.25;
    private const double PoorFitGain = 0.75;
    private const double GoodFitCost = 0.75;
    private const double PoorFitCost = 1.25;

    // İstila temposu plansız 1 (MatchRun.Invade.cs); planın bedeli bunun üstündeki kısım.
    private const double BaseInvadeTempo = 1;

    // Bot'a gank: plansız her koridor 1/3.
    private const double EvenLaneShare = 1.0 / 3;

    public TeamTactics(GamePlan plan, TeamDraft team)
    {
        Plan = plan;
        Fit = PlanFits.Of(team, plan);
        Knobs = PlanKnobs.Of(plan);
        (Gain, Cost) = Fit switch
        {
            PlanFit.Good => (GoodFitGain, GoodFitCost),
            PlanFit.Poor => (PoorFitGain, PoorFitCost),
            _ => (1.0, 1.0),
        };
    }

    public GamePlan Plan { get; }
    public PlanFit Fit { get; }
    public PlanKnobs Knobs { get; }

    /// <summary>Kazanç çarpanı: uyan 1,25, nötr 1, uymayan 0,75.</summary>
    public double Gain { get; }

    /// <summary>Bedel çarpanı: uyan 0,75, nötr 1, uymayan 1,25.</summary>
    public double Cost { get; }

    // Örnek: Ejder & late, uyan draft → ejder savaşında d +4,5 × 1,25 ≈ +5,6, Herald'da −4,5 × 0,75 ≈ −3,4;
    // uymayan → ejderde +3,4, Herald'da −5,6.
    public double DragonFightBonus => Knobs.DragonFightBonus * Gain;
    public double EarlySkirmishBonus(int minute) => minute < Knobs.EarlySkirmishUntil ? Knobs.EarlySkirmishBonus * Gain : 0;
    public double ExtraGankChance => Math.Min(1, Knobs.ExtraGankChance * Gain);
    public double BotGankSuccessBonus => Knobs.BotGankSuccessBonus * Gain;
    public double SplitChanceBonus => Knobs.SplitChanceBonus * Gain;
    public double AnswerChanceCut => Knobs.AnswerChanceCut * Gain;
    public double DuelBonus => Knobs.DuelBonus * Gain;
    public double SiegeLeadFactor => 1 - Knobs.SiegeLeadDiscount * Gain;
    public double InvadeFightBonus => Knobs.InvadeFightBonus * Gain;

    // Düzen eğilimi: Split planında 1-4 / 1-3-1 ağırlığı ×2 (uyanda ×2,25, uymayanda ×1,75); Dengeli'de ×1.
    public double SplitLayoutFactor => 1 + Knobs.SplitLayoutTilt * Gain;
    public double GroupLayoutFactor => 1 + Knobs.GroupLayoutTilt * Gain;

    public double HeraldFightPenalty => Knobs.HeraldFightPenalty * Cost;
    public double TeamFightPenalty => Knobs.TeamFightPenalty * Cost;
    public double SiegeDefenseExtra => Knobs.SiegeDefenseExtra * Cost;

    /// <summary>Püskürtülen istilada rakibin temposu: 1 + plan bedeli (2,5 → uyanda ~2,1, uymayanda ~2,9); plansız 0.</summary>
    public double InvadeLossTempo =>
        Knobs.InvadeLossTempo <= 0 ? 0 : BaseInvadeTempo + (Knobs.InvadeLossTempo - BaseInvadeTempo) * Cost;

    /// <summary>Bot'a gitme ihtimali: kazanç 1/3'ün üstündeki kısım (0,6 → uyanda 0,67, uymayanda 0,53).</summary>
    public double BotGankShare => Knobs.BotGankShare <= 0 ? 0 : EvenLaneShare + (Knobs.BotGankShare - EvenLaneShare) * Gain;

    /// <summary>Gücün plan çarpanı (Dengeli'de tam 1: sonuç bit bit aynı).</summary>
    public double PowerFactor(int minute) =>
        (Knobs.LatePowerGain > 0 && minute >= Knobs.LatePowerFrom ? 1 + Knobs.LatePowerGain * Gain : 1)
        * (Knobs.LatePowerLoss > 0 && minute >= Knobs.LatePowerLossFrom ? 1 - Knobs.LatePowerLoss * Cost : 1);

    /// <summary>Bu takımın koridor akışına (kendi gözünden) eklenen; negatif = geride.</summary>
    // Kazançlar uyumla (Gain), bedeller ters (Cost). Örnek: Bot baskısı, uyan draft, 8. dk → bot +0,31, top −0,19.
    public double LaneShift(Lane lane, int minute) =>
        (minute < Knobs.LaneGainUntil ? Knobs.LaneGain * Gain : 0)
        + (lane == Lane.Bot && minute < Knobs.BotGainUntil ? Knobs.BotLaneGain * Gain : 0)
        - (minute < Knobs.LanePenaltyUntil ? Knobs.LanePenalty * Cost : 0)
        - (lane == Lane.Top && minute < Knobs.TopPenaltyUntil ? Knobs.TopLanePenalty * Cost : 0);
}

/// <summary>
/// Planın draft'a uyumu (DESIGN.md §4.10). Ölçütler şampiyon profilinden; arayüz de aynısını gösterir. Eşikler havuza
/// göre: her plan rastgele draft'ların ~%25-33'üne uyar, ~%20-33'üne uymaz (170'lik havuz, 100.000 rastgele takım).
/// </summary>
internal static class PlanFits
{
    // Takımın geç − erken toplamı: havuz ortalaması +2,65 (havuz geç ağırlıklı). Ortanın 4 üstü (≥ 7) "geç
    // ağırlıklı" (~%27), 4 altı (≤ −1) "erken ağırlıklı" (~%28).
    private const int LateLeaning = 7;
    private const int EarlyLeaning = -1;

    public static PlanFit Of(TeamDraft team, GamePlan plan)
    {
        // Örnek: Kayle (2-6-10), Kassadin, Jinx'li takım geç − erken +23 → Ejder & late uyar, Erken baskı uymaz.
        var lateLean = team.Champions.Sum(c => c.Late - c.Early);
        var poke = team.Champions.Sum(c => c.Tags.Poke);
        var botEarly = (team.Bot.Early + team.Support.Early) / 2.0;
        var sideSplit = team.Top.Tags.Split + team.Mid.Tags.Split;

        return plan switch
        {
            GamePlan.Balanced => PlanFit.Neutral,
            GamePlan.DragonLate => Lean(lateLean >= LateLeaning, lateLean <= EarlyLeaning),
            GamePlan.EarlyPressure => Lean(lateLean <= EarlyLeaning, lateLean >= LateLeaning),
            // Bot ikilisinin erken ortalaması: ≥ 7 %29, ≤ 5,5 %33.
            GamePlan.BotPressure => Lean(botEarly >= 7, botEarly <= 5.5),
            // Top + mid split toplamı: ≥ 3 %27, 0 (hiç splitçi yok, plan işe yaramaz) %20.
            GamePlan.Split => Lean(sideSplit >= 3, sideSplit == 0),
            // Poke'lu (≥ 7) ya da erken ağırlıklı (≤ −2) %31; geç ağırlıklı (≥ 5) ve poke'suz (≤ 4) %25.
            GamePlan.FastFinish => Lean(poke >= 7 || lateLean <= -2, lateLean >= 5 && poke <= 4),
            // Ormancının erken gücü: ≥ 8 %30, ≤ 5 %25.
            GamePlan.Invade => Lean(team.Jungle.Early >= 8, team.Jungle.Early <= 5),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
        };
    }

    private static PlanFit Lean(bool good, bool poor) => good ? PlanFit.Good : poor ? PlanFit.Poor : PlanFit.Neutral;
}
