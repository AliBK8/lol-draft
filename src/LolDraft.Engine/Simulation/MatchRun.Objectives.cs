using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Objektif savaşları ve dk 35 son savaş (DESIGN.md §4.5 Objektifler).</summary>
internal sealed partial class MatchRun
{
    private const int FinalMinute = 35;
    private const int ObjectiveSecond = 30;

    // Takım savaşı ayarları (DESIGN.md §4.5).
    private const double FightScoreFactor = 0.2;
    private const double FightDivisor = 15;
    private const double ComebackMultiplier = 1.25;

    // Sabit takvim (prototip). Port sonrası ejder ruhu/Elder kuralıyla dinamik olacak (DESIGN.md §4.5).
    private static readonly Dictionary<int, Objective> ObjectiveSchedule = new()
    {
        [6] = Objective.Dragon, [11] = Objective.Dragon, [14] = Objective.Herald, [16] = Objective.Dragon,
        [21] = Objective.Dragon, [23] = Objective.Baron, [27] = Objective.Dragon, [30] = Objective.Elder,
        [FinalMinute] = Objective.FinalFight,
    };

    // Bu dakika objektif varsa oynar; son savaşsa maçı bitirip sonucu döner.
    // Zar sırası: kazanan, sonra savaş kill'leri.
    private MatchEnd? PlayObjective(int minute)
    {
        if (!ObjectiveSchedule.TryGetValue(minute, out var objective))
            return null;

        var totalBefore = _scores.Total;
        var t = (minute - 1) * 60 + ObjectiveSecond;
        var blueProbability = FightProbability(TeamSide.Blue, minute);
        var winner = _rng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var winnerProbability = winner == TeamSide.Blue ? blueProbability : 1 - blueProbability;

        // Geri dönüş: puanda geride olan takım kazanırsa değer ×1.25 (işaretler ters → çarpım negatif).
        var value = Value(objective);
        var comeback = totalBefore * winner.Sign() < 0;
        if (comeback)
            value *= ComebackMultiplier;
        _scores.Add(Category(objective), winner.Sign() * value);

        var text = objective == Objective.FinalFight
            ? $"Son takım savaşını {winner.DisplayName()} kazandı"
            : $"{winner.DisplayName()} {Accusative(objective)} aldı{(comeback ? " (geri dönüş, ekstra puan)" : "")}";
        _events.Add(new ObjectiveEvent(t, text, objective, winner, winnerProbability));
        FightKills(winner, t, MaxKills(objective));

        if (objective != Objective.FinalFight)
            return null;

        // Son savaşı kazanan Nexus'a yürür: görünmez toplanma, sonra Nexus.
        _events.Add(new SiegeEvent(t + 25, "", winner, Lead: 20, Tail: 6));
        _events.Add(new NexusEvent(t + 28, $"{winner.DisplayName()} son savaştan sonra Nexus'a yürüdü, maç bitti", winner));
        return new MatchEnd(minute, winner, MatchEndKind.FinalFight, winnerProbability);
    }

    // Savaş kill'leri (DESIGN.md §4.5): kazananın yaşayanları kaybedenin yaşayanlarından 1..max kişi keser.
    // Kurban ve katil zarla seçilir; kill'ler 1.5 sn arayla t+2'den başlar. Zar sırası: önce kurban, sonra katil.
    private void FightKills(TeamSide winner, double t, int maxKills)
    {
        var winners = AliveAt(winner, t);
        var losers = AliveAt(winner.Opponent(), t);
        var killCount = Math.Min(losers.Count, 1 + _rng.NextIndex(maxKills));

        for (var i = 0; i < killCount && winners.Count > 0; i++)
        {
            var victimIndex = _rng.NextIndex(losers.Count);
            var victim = losers[victimIndex];
            losers.RemoveAt(victimIndex);
            var killer = winners[_rng.NextIndex(winners.Count)];

            Kill(winner, killer, victim, t + 2 + i * 1.5, $"Takım savaşında {victim.Name} düştü, kill {killer.Name} aldı");
        }
    }

    // Takım savaşı ihtimali (DESIGN.md §4.5):
    //   d      = (Takım gücü Mavi − Takım gücü Kırmızı) + sinerji farkı + 0.2 × toplam puan
    //   p_Mavi = clamp(1 / (1 + e^(−d/15)), 0.1, 0.9)
    // Bölen 15 eğrinin dikliği: d = 15 → ~%73. Puan sadece ihtimali kaydırır, sonucu zar belirler.
    // Kırmızı için d'nin işareti çevrilir: p_Kırmızı = 1 − p_Mavi (clamp simetrik olduğu için aynı kalır).
    private double FightProbability(TeamSide side, int minute)
    {
        var d = (TeamPower(_blue, minute) - TeamPower(_red, minute)) + _synergyDiff + FightScoreFactor * _scores.Total;
        return Probability.Clamp(Probability.Logistic(side.Sign() * d / FightDivisor));
    }

    // Takım gücü: 5 şampiyonun G toplamı (DESIGN.md §4.3).
    private static int TeamPower(TeamDraft team, int minute) => team.Champions.Sum(c => c.PowerAt(minute));

    private static double Value(Objective objective) => objective switch
    {
        Objective.Dragon => 4,
        Objective.Herald => 4,
        Objective.Baron => 8,
        Objective.Elder => 12,
        Objective.FinalFight => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };

    private static int MaxKills(Objective objective) => objective switch
    {
        Objective.Dragon => 2,
        Objective.FinalFight => 4,
        _ => 3,
    };

    private static ScoreCategory Category(Objective objective) => objective switch
    {
        Objective.Dragon => ScoreCategory.Dragon,
        Objective.Herald => ScoreCategory.Herald,
        Objective.Baron => ScoreCategory.Baron,
        Objective.Elder => ScoreCategory.Elder,
        Objective.FinalFight => ScoreCategory.Final,
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };

    // "Mavi ejderi aldı", "Kırmızı Baron'u aldı"
    private static string Accusative(Objective objective) => objective switch
    {
        Objective.Dragon => "ejderi",
        Objective.Herald => "Herald'ı",
        Objective.Baron => "Baron'u",
        Objective.Elder => "Elder ejderini",
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };
}
