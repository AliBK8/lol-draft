using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Nehir çatışması (DESIGN.md §4.5). Prototiple aynı; aralıklı dakika ve eşitsiz sayı port sonrası gelecek.</summary>
internal sealed partial class MatchRun
{
    private const double SkirmishChance = 0.75;
    private const double SkirmishScoreFactor = 0.1;
    private const double SkirmishDivisor = 6;
    private const double SkirmishValue = 3;
    private const int SkirmishMaxKills = 2;
    private const int SkirmishMoveLead = 14;
    private const int SkirmishMoveTail = 4;

    private static readonly HashSet<int> SkirmishMinutes = [8, 13, 18, 26];

    // Kimin çatışmaya geleceği: nehre en yakın roller önce (ormancı, support, mid, adc, top).
    private static ChampionProfile[] SkirmishPriority(TeamDraft team) =>
        [team.Jungle, team.Support, team.Mid, team.Bot, team.Top];

    // Zar sırası: olacak mı, kişi sayısı, saniye, kazanan, nehir yarısı, kill sayısı, (her kill için) kurban ve katil.
    private void PlaySkirmish(int minute)
    {
        if (!SkirmishMinutes.Contains(minute) || _rng.NextDouble() >= SkirmishChance)
            return;

        var size = _rng.NextDouble() < 0.5 ? 2 : 3;
        var t = (minute - 1) * 60 + 15 + _rng.NextDouble() * 15;

        // Her taraftan öncelik sırasıyla yaşayan ilk 'size' kişi gelir; ölüler yüzünden sayılar eşit olmayabilir.
        var blue = SkirmishPriority(_blue).Where(c => _deaths.IsAlive(c, t)).Take(size).ToList();
        var red = SkirmishPriority(_red).Where(c => _deaths.IsAlive(c, t)).Take(size).ToList();
        if (blue.Count == 0 || red.Count == 0)
            return;

        // Çatışma ihtimali (DESIGN.md §4.5):
        //   d      = ΣG_Mavi − ΣG_Kırmızı + 0.1 × toplam puan
        //   p_Mavi = clamp(1 / (1 + e^(−d/6)))
        // Bölen 6 (takım savaşında 15): az kişide güç farkı daha belirleyici. Kişi sayısı farkı ΣG'ye yansır.
        var d = blue.Sum(c => c.PowerAt(minute)) - red.Sum(c => c.PowerAt(minute)) + SkirmishScoreFactor * _scores.Total;
        var blueProbability = Probability.Clamp(Probability.Logistic(d / SkirmishDivisor));
        var winner = _rng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var winnerProbability = winner == TeamSide.Blue ? blueProbability : 1 - blueProbability;
        _scores.Add(ScoreCategory.Skirmish, winner.Sign() * SkirmishValue);

        var river = _rng.NextDouble() < 0.5 ? RiverSide.Top : RiverSide.Bottom;
        string[] parts = [.. blue.Select(c => c.Id), .. red.Select(c => c.Id)];
        _events.Add(new MoveEvent(t, "", parts, Lane: null, river, SkirmishMoveLead, SkirmishMoveTail));
        _events.Add(new SkirmishEvent(t, $"Nehirde {blue.Count}v{red.Count} çatışma, {winner.DisplayName()} kazandı",
            winner, winnerProbability, river, blue.Count, red.Count));

        // Kaybedenlerden 1-2 kill (kaybeden tek kişiyse 1). Kill'ler 1.5 sn arayla t+1'den başlar.
        var winners = winner == TeamSide.Blue ? blue : red;
        var losers = (winner == TeamSide.Blue ? red : blue).ToList();
        var killCount = 1 + _rng.NextIndex(Math.Min(SkirmishMaxKills, losers.Count));

        for (var i = 0; i < killCount; i++)
        {
            var victimIndex = _rng.NextIndex(losers.Count);
            var victim = losers[victimIndex];
            losers.RemoveAt(victimIndex);
            var killer = winners[_rng.NextIndex(winners.Count)];

            Kill(winner, killer, victim, t + 1 + i * 1.5, $"{killer.Name}, {victim.Name} karakterini nehirde yakaladı");
        }
    }
}
