using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Ormancı istilası (DESIGN.md §4.5 Orman): 4:05-4:40'ta, rakibin yeniden doğan küçük kampına baskın. Rakibin ilk turdan
/// temizlediği küçük kamplar ~2:15 sonra bu aralıkta doğar; gank pencereleriyle (3:xx, 5-6. dk) çakışmaz.
/// Kendi altınını icat etmez: çalınan kamp farm (CS), "ormancı geride" orman akışına tempo (farm kuralı altına çevirir),
/// çatışma kill'leri kill ve ödül kurallarından.
/// </summary>
internal sealed partial class MatchRun
{
    // Dakika 5 = 4:00-4:59; saniye 5-40. Dengeli oyunda maçların %20'sinde (oyun planı ileride artırır, §4.10).
    private const int InvadeMinute = 5;
    private const double InvadeChance = 0.2;
    private const double InvadeMinSecond = 5;
    private const double InvadeSecondSpan = 35;

    // Yanına mid ya da support alma (iki taraf için ayrı zar); rakip ormancının cevap vermesi.
    private const double InvadeHelperChance = 0.5;
    private const double InvadeAnswerChance = 0.6;

    // Çalınan kamp: istilacı ormancıya 4 CS (80 altın). Kazanan 3 dakika orman akışında +1 tempo: kaybeden ormancı
    // farmının %25'ini kaçırır, kazanan yarısını alır (SplitCs). 3 dakikada ~6 CS kayıp, ~3 CS kazanç.
    private const double StolenCampCs = 4;
    private const double InvadeTempo = 1;
    private const int InvadeTempoMinutes = 3;

    private const int InvadeMaxKills = 2;
    private const int InvadeMoveLead = 16;
    private const int InvadeMoveTail = 4;

    private static readonly JungleCamp[] InvadeCamps = [JungleCamp.Raptors, JungleCamp.Wolves, JungleCamp.Krugs];

    // Orman akışına istilanın temposu (+ = Mavi), _invadeTempoUntil dakikasına kadar (dahil).
    private double _invadeTempo;
    private int _invadeTempoUntil;

    private double InvadeTempoAt(int minute) => minute <= _invadeTempoUntil ? _invadeTempo : 0;

    // ScheduleJungle'ın sonunda çağrılır. Zar sırası (dk 5'in başında): istila var mı, (varsa) saniye.
    private void ScheduleInvade(int minute, double junglePowerDiff)
    {
        if (minute != InvadeMinute || _rng.NextDouble() >= InvadeChance)
            return;

        var t = (minute - 1) * 60 + InvadeMinSecond + _rng.NextDouble() * InvadeSecondSpan;
        At(t, () => PlayInvade(minute, t, junglePowerDiff));
    }

    // Zar sırası: istila eden taraf, kamp, istilacının yardımcısı (gelir mi, kim), (rakip ormancı müsaitse) cevap,
    // (cevap varsa) savunanın yardımcısı ve kazanan; kill sayısı. Kill'ler kendi saniyelerinde.
    private void PlayInvade(int minute, double t, double junglePowerDiff)
    {
        // İstila eden: güçlü erken ormancı daha olası (gank'ı atan tarafla aynı eğri, %10-%90).
        var blueProbability = Probability.Clamp(Probability.Logistic(junglePowerDiff / GankSideDivisor));
        var side = _rng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var camp = InvadeCamps[_rng.NextIndex(InvadeCamps.Length)];
        var us = Team(side);
        var them = Team(side.Opponent());

        // İstilacı ormancı ölü ya da meşgulse istila yok.
        if (!IsInLane(us.Jungle, t))
            return;

        List<ChampionProfile> invaders = [us.Jungle, .. InvadeHelper(us, t)];
        var answered = IsInLane(them.Jungle, t) && _rng.NextDouble() < InvadeAnswerChance;
        if (!answered)
        {
            // Cevapsız: kamp çalınır. Katılanlar bir süre koridorlarında değil (yardımcı minyon kaçırır).
            MarkAway(invaders, t + AwayAfterFightSeconds);
            AddFarm(us.Jungle, StolenCampCs);
            SetInvadeTempo(side, minute);
            _events.Add(new InvadeEvent(t,
                $"{Names(invaders)} {Possessive(side.Opponent())} ormanına girdi, {CampAccusative(camp)} çaldı",
                side, camp, [.. invaders.Select(c => c.Id)], [], Stolen: true, Probability: null, InvadeMoveLead, InvadeMoveTail));
            return;
        }

        List<ChampionProfile> defenders = [them.Jungle, .. InvadeHelper(them, t)];

        // Çatışma (nehir çatışmasıyla aynı hesap, DESIGN.md §16.7):
        //   d = ΣG_eff_Mavi − ΣG_eff_Kırmızı + K7 (pick farkı) · p_Mavi = clamp(1 / (1 + e^(−d/6)))
        // Örnek: 2v1'de G=6'lık şampiyonlar → d = ±6 → ~%73 kalabalık taraf.
        var (blue, red) = side == TeamSide.Blue ? (invaders, defenders) : (defenders, invaders);
        var rules = TeamRules.Effects(FightKind.Skirmish, blue, red, minute, c => EffectivePower(c, minute, t));
        var d = EffectivePower(blue, minute, t) - EffectivePower(red, minute, t) + rules.Sum(e => e.Signed);
        ReportFightRules(rules, RuleContext.Skirmish, d, rules.Sum(e => e.Signed), SkirmishDivisor);
        var blueWins = Probability.Clamp(Probability.Logistic(d / SkirmishDivisor));
        var winner = _rng.NextDouble() < blueWins ? TeamSide.Blue : TeamSide.Red;
        var invaderProbability = side == TeamSide.Blue ? blueWins : 1 - blueWins;
        var stolen = winner == side;

        MarkAway([.. invaders, .. defenders], t + AwayAfterFightSeconds);
        if (stolen)
            AddFarm(us.Jungle, StolenCampCs);
        SetInvadeTempo(winner, minute);

        var text = stolen
            ? $"{Names(invaders)} {Possessive(side.Opponent())} ormanına girdi, {Names(defenders)} yetişti ama "
              + $"{side.DisplayName()} kazandı, {CampAccusative(camp)} çaldı"
            : $"{Names(invaders)} {Possessive(side.Opponent())} ormanına girdi, {Names(defenders)} istilayı püskürttü";
        _events.Add(new InvadeEvent(t, text, side, camp, [.. invaders.Select(c => c.Id)], [.. defenders.Select(c => c.Id)],
            stolen, invaderProbability, InvadeMoveLead, InvadeMoveTail));

        // Kaybedenlerden 1-2 kill (kaybeden tek kişiyse 1). Kill'ler 1,5 sn arayla t+1'den.
        var (winners, losers) = stolen ? (invaders, defenders) : (defenders, invaders);
        var killCount = 1 + _rng.NextIndex(Math.Min(InvadeMaxKills, losers.Count));
        ScheduleKills(winner, winners, losers, killCount, t + 1,
            (killer, victim) => $"{killer.Name}, {victim.Name} karakterini ormanda yakaladı");
    }

    // Yardımcı: %50 gelir; gelirse mid ya da support (eşit). Zar her durumda atılır (sıra kaymasın); koridorunda değilse
    // (ölü, meşgul) gelmez.
    private IEnumerable<ChampionProfile> InvadeHelper(TeamDraft team, double t)
    {
        var comes = _rng.NextDouble() < InvadeHelperChance;
        var helper = _rng.NextIndex(2) == 0 ? team.Mid : team.Support;
        return comes && IsInLane(helper, t) ? [helper] : [];
    }

    private void SetInvadeTempo(TeamSide winner, int minute)
    {
        _invadeTempo = winner.Sign() * InvadeTempo;
        _invadeTempoUntil = minute + InvadeTempoMinutes;
    }

    private static string Names(IReadOnlyList<ChampionProfile> champions) => string.Join(" ve ", champions.Select(c => c.Name));

    private static string Possessive(TeamSide side) => side == TeamSide.Blue ? "Mavi'nin" : "Kırmızı'nın";

    private static string CampAccusative(JungleCamp camp) => camp switch
    {
        JungleCamp.Raptors => "kuşları",
        JungleCamp.Wolves => "kurtları",
        JungleCamp.Krugs => "kurbağaları",
        _ => throw new ArgumentOutOfRangeException(nameof(camp)),
    };
}
