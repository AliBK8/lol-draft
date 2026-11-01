using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Objektif savaşları, ejder ruhu ve dk 35 son savaş (DESIGN.md §4.5 Objektifler).</summary>
internal sealed partial class MatchRun
{
    private const int FinalMinute = 35;
    private const int ObjectiveSecond = 30;

    // Takım savaşı ayarları (DESIGN.md §4.5).
    private const double FightScoreFactor = 0.2;
    private const double FightDivisor = 15;
    private const double ComebackMultiplier = 1.25;

    // Ejder ritmi (gerçek oyun): ilk ejder 5:00'te doğar (savaş 5:30 = 6. dakika), alındıktan 5 dk sonra yenisi.
    // Bir takım 4 ejdere ulaşınca ejder ruhu: ejder doğmaz, 6 dk sonra Elder; Elder alınınca 6 dk sonra tekrar.
    private const int FirstDragonMinute = 6;
    private const int DragonRespawnMinutes = 5;
    private const int DragonsForSoul = 4;
    private const int ElderRespawnMinutes = 6;

    // Ejder ruhu (ayar düğmeleri): bir kerelik puan + maçın geri kalanında takım savaşı d'sine bonus.
    // Ölçülü tutuldu: eşit maçta ruh tek başına savaşı ~%60'a çıkarır, maçı bitirmez.
    private const double SoulValue = 6;
    private const double SoulFightBonus = 2;

    // Dinamik takvim: dakika → objektif. Herald, Baron ve son savaş sabit; ejder ve Elder alındıkça planlanır.
    private readonly Dictionary<int, Objective> _objectiveAt = new()
    {
        [FirstDragonMinute] = Objective.Dragon, [14] = Objective.Herald, [23] = Objective.Baron,
        [FinalMinute] = Objective.FinalFight,
    };

    private readonly int[] _dragons = new int[2];
    private TeamSide? _soul;

    // Bu dakika objektif varsa 30. saniyeye kaydolur.
    private void ScheduleObjective(int minute)
    {
        if (_objectiveAt.TryGetValue(minute, out var objective))
            At((minute - 1) * 60 + ObjectiveSecond, () => PlayObjective(minute, objective));
    }

    // Objektifi ileri bir dakikaya planlar. O dakika doluysa bir sonrakine kayar; son savaşa (ve sonrasına) düşerse çıkmaz.
    private void PlanObjective(int minute, Objective objective)
    {
        while (_objectiveAt.ContainsKey(minute) && minute < FinalMinute)
            minute++;
        if (minute < FinalMinute)
            _objectiveAt[minute] = objective;
    }

    // Ejder/Elder alındıktan sonra sıradakini planlar; 4. ejderde ruhu verir.
    private void AfterDragonOrElder(Objective objective, TeamSide winner, int minute, double t)
    {
        if (objective == Objective.Elder)
        {
            PlanObjective(minute + ElderRespawnMinutes, Objective.Elder);
            return;
        }

        if (++_dragons[(int)winner] < DragonsForSoul)
        {
            PlanObjective(minute + DragonRespawnMinutes, Objective.Dragon);
            return;
        }

        _soul = winner;
        _scores.Add(ScoreCategory.Dragon, winner.Sign() * SoulValue);
        _events.Add(new DragonSoulEvent(t, $"{winner.DisplayName()} ejder ruhunu kazandı!", winner));
        PlanObjective(minute + ElderRespawnMinutes, Objective.Elder);
    }

    // Zar sırası: kazanan, kill sayısı (kill'ler kendi saniyelerinde). Son savaşsa Nexus'u kaydeder.
    private void PlayObjective(int minute, Objective objective)
    {
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
        if (objective is Objective.Dragon or Objective.Elder)
            AfterDragonOrElder(objective, winner, minute, t);

        // Savaşa iki takımın yaşayanları gelir; bir süre koridorlarında değiller.
        var losers = AliveAt(winner.Opponent(), t);
        MarkAway([.. AliveAt(winner, t), .. losers], t + AwayAfterFightSeconds);
        ScheduleFightKills(winner, t, MaxKills(objective));

        if (objective != Objective.FinalFight)
        {
            SchedulePostFightPush(winner, objective, losers, t);
            return;
        }

        // Son savaşı kazanan Nexus'a yürür: görünmez toplanma, sonra Nexus ve maç sonu.
        At(t + 25, () => _events.Add(new SiegeEvent(t + 25, "", winner, Lead: 20, Tail: 6)));
        At(t + 28, () =>
        {
            _events.Add(new NexusEvent(t + 28, $"{winner.DisplayName()} son savaştan sonra Nexus'a yürüdü, maç bitti", winner));
            EndMatch(new MatchEnd(minute, winner, MatchEndKind.FinalFight, winnerProbability));
        });
    }

    // Savaş kill'leri (DESIGN.md §4.5): kazananın yaşayanları kaybedenin yaşayanlarından 1..max kişi keser.
    // Katılımcılar ve kill sayısı savaş anında belirlenir; kill'ler 1.5 sn arayla t+2'den başlar.
    private void ScheduleFightKills(TeamSide winner, double t, int maxKills)
    {
        var winners = AliveAt(winner, t);
        var losers = AliveAt(winner.Opponent(), t);
        var killCount = Math.Min(losers.Count, 1 + _rng.NextIndex(maxKills));

        ScheduleKills(winner, winners, losers, killCount, t + 2,
            (killer, victim) => $"Takım savaşında {victim.Name} düştü, kill {killer.Name} aldı");
    }

    // Takım savaşı ihtimali (DESIGN.md §4.5):
    //   d      = (Takım gücü Mavi − Takım gücü Kırmızı) + sinerji farkı + 0.2 × toplam puan + ruh bonusu (±2)
    //   p_Mavi = clamp(1 / (1 + e^(−d/15)), 0.1, 0.9)
    // Bölen 15 eğrinin dikliği: d = 15 → ~%73. Puan sadece ihtimali kaydırır, sonucu zar belirler.
    // Kırmızı için d'nin işareti çevrilir: p_Kırmızı = 1 − p_Mavi (clamp simetrik olduğu için aynı kalır).
    private double FightProbability(TeamSide side, int minute)
    {
        var soulBonus = _soul is { } soul ? soul.Sign() * SoulFightBonus : 0;
        var d = (TeamPower(_blue, minute) - TeamPower(_red, minute)) + _synergyDiff + FightScoreFactor * _scores.Total
                + soulBonus;
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
