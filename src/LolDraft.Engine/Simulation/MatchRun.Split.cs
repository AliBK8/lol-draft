using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Split (DESIGN.md §16.8 K12): takım savaşını kaybedeceğini düşünen takım, haritanın öbür ucunda karşılık arar.
/// Rakip ya cevap verir (objektif 4v4, yan koridorda düello) ya da bedelini öder (objektif 4v5, kule karşılıksız düşer).
/// </summary>
internal sealed partial class MatchRun
{
    // Kim gidebilir: top ya da mid, split ≥ 2, koridorunda, rakibini 1v1 yeniyor (G_eff'i yüksek). dk ≥ 14.
    // Elder ve son savaşta herkes toplanır (maçı bitiren savaşlarda kimse dışarıda kalmaz).
    private const int SplitMinMinute = 14;
    private const int SplitMinTag = 2;

    // Gidiyor mu: sadece 5v5 ihtimali < %50 olan takım; split 2 → %40, split 3 → %60.
    private const double SplitBaseChance = 0.4;
    private const double SplitChancePerTag = 0.2;

    // Rakip cevap veriyor mu: aynı roldeki rakip koridordaysa %50.
    private const double AnswerChance = 0.5;

    // Düello: çatışma gibi (bölen 6), objektiften 6 sn sonra. Kule: objektiften 10 sn sonra (savaş sonrası itiş 12'de).
    private const double DuelDivisor = 6;
    private const double DuelDelay = 6;
    private const double SplitTowerDelay = 10;

    private sealed record Split(TeamSide Side, ChampionProfile Splitter, Lane Lane, ChampionProfile? Answerer)
    {
        // Objektif savaşında olmayanlar.
        public ChampionProfile[] Absent => Answerer is null ? [Splitter] : [Splitter, Answerer];
    }

    // Objektif savaşından hemen önce: split olacak mı. Zar sırası: gidiyor mu, (rakip koridordaysa) cevap veriyor mu.
    private Split? PlanSplit(int minute, Objective objective, double t)
    {
        if (minute < SplitMinMinute || objective is Objective.Elder or Objective.FinalFight)
            return null;

        var blueProbability = FightProbability(TeamSide.Blue, minute, t, FightKind.Objective);
        if (blueProbability == 0.5)
            return null;

        var side = blueProbability < 0.5 ? TeamSide.Blue : TeamSide.Red;
        var team = Team(side);
        var splitter = new[] { team.Top, team.Mid }
            .Where(c => c.Tags.Split >= SplitMinTag && IsInLane(c, t)
                        && EffectivePower(c, minute, t) > EffectivePower(Counterpart(c), minute, t))
            .MaxBy(c => c.Tags.Split);
        if (splitter is null || _rng.NextDouble() >= SplitChance(splitter))
            return null;

        var rival = Counterpart(splitter);
        var answerer = IsInLane(rival, t) && _rng.NextDouble() < AnswerChance ? rival : null;
        return new Split(side, splitter, splitter == team.Top ? Lane.Top : Lane.Mid, answerer);
    }

    internal static double SplitChance(ChampionProfile splitter) =>
        SplitBaseChance + SplitChancePerTag * (splitter.Tags.Split - SplitMinTag);

    // Split olayı objektifle aynı saniyede; splitter ve cevap veren savaş süresince koridor anlatıcısına kapalı.
    private void PlaySplit(Split split, int minute, double t)
    {
        var (side, splitter, lane, answerer) = split;
        var answer = answerer is null ? "kimse cevap vermedi" : $"{answerer.Name} cevap verdi";
        _events.Add(new SplitEvent(t, $"{splitter.Name} {LaneName(lane)} koridorda split yapıyor, {answer}",
            side, lane, splitter.Id, answerer?.Id));
        MarkAway(split.Absent, t + AwayAfterFightSeconds);

        if (answerer is null)
        {
            At(t + SplitTowerDelay, () => SplitTower(split, t + SplitTowerDelay));
            return;
        }

        At(t + DuelDelay, () => PlayDuel(split, answerer, minute, t));
    }

    // Düello (objektiften 6 sn sonra): p_splitter = clamp(1 / (1 + e^(−(G_eff farkı)/6))). Splitter kazanırsa kule de
    // onun (objektiften 10 sn sonra). Zar sırası: kazanan.
    private void PlayDuel(Split split, ChampionProfile answerer, int minute, double fightT)
    {
        var (side, splitter, lane, _) = split;
        var t = fightT + DuelDelay;
        if (!_deaths.IsAlive(splitter, t) || !_deaths.IsAlive(answerer, t))
            return;

        var d = EffectivePower(splitter, minute, t) - EffectivePower(answerer, minute, t);
        var splitterWins = _rng.NextDouble() < Probability.Clamp(Probability.Logistic(d / DuelDivisor));
        var (winSide, killer, victim) = splitterWins ? (side, splitter, answerer) : (side.Opponent(), answerer, splitter);
        Kill(winSide, killer, victim, t, $"{killer.Name} yan koridorda düelloyu kazandı, {victim.Name} düştü", lane);

        if (splitterWins)
            At(fightT + SplitTowerDelay, () => SplitTower(split, fightT + SplitTowerDelay));
    }

    // Splitter kuleyi alır: rakip ya savaşta ya ölü, normal kule şartları aranmaz. Koridorda kule kalmadıysa yok.
    private void SplitTower(Split split, double t)
    {
        var (side, splitter, lane, _) = split;
        if (_deaths.IsAlive(splitter, t) && _towersDestroyed[(int)side, (int)lane] < TowersPerLane)
            TakeTower(side, lane, t, $"{splitter.Name} split'te {LaneName(lane)} koridorda", scored: true);
    }
}
