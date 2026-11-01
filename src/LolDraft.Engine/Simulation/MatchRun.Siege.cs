using LolDraft.Engine.Events;

namespace LolDraft.Engine.Simulation;

/// <summary>Kuşatma ve throw (DESIGN.md §4.6): önde olan takım maçı erken bitirmeye gider.</summary>
internal sealed partial class MatchRun
{
    private const int SiegeMinMinute = 20;
    private const double SiegeThreshold = 45;
    private const int SiegeCooldownMinutes = 3;
    private const int SiegeSecond = 45;
    private const double ThrowValue = 10;
    private const int SiegeMaxKills = 4;
    private const int ThrowMaxKills = 3;

    // -9: maç başında "son kuşatma çok önceydi" (JS: lastSiege = -9).
    private int _lastSiegeMinute = -9;

    // Kuşatma şartları: dk ≥ 20, o dakika objektif yok, |toplam| ≥ 45, son kuşatmadan bu yana ≥ 3 dk.
    // Zar sırası: başarı, sonra savaş kill'leri.
    private MatchEnd? PlaySiege(int minute)
    {
        var total = _scores.Total;
        if (minute < SiegeMinMinute || ObjectiveSchedule.ContainsKey(minute)
            || Math.Abs(total) < SiegeThreshold || minute - _lastSiegeMinute < SiegeCooldownMinutes)
            return null;

        _lastSiegeMinute = minute;
        var attacker = total > 0 ? TeamSide.Blue : TeamSide.Red;
        var defender = attacker.Opponent();
        var t = (minute - 1) * 60 + SiegeSecond;
        // Başarı ihtimali = takım savaşı ihtimali, önde olanın tarafından.
        var probability = FightProbability(attacker, minute);

        _events.Add(new SiegeEvent(t, "", attacker, Lead: 20, Tail: 4));

        if (_rng.NextDouble() < probability)
        {
            FightKills(attacker, t, SiegeMaxKills);
            _events.Add(new NexusEvent(t + 8,
                $"{attacker.DisplayName()} kuşatmayı kazandı ve Nexus'u yıktı, maç {minute}. dakikada bitti", attacker, probability));
            return new MatchEnd(minute, attacker, MatchEndKind.Siege, probability);
        }

        // Throw: savunan kuşatmayı tutar, +10 puan ve kill'ler alır. Önde olanın avantajı azalır, maç sürer.
        _scores.Add(ScoreCategory.Siege, defender.Sign() * ThrowValue);
        FightKills(defender, t, ThrowMaxKills);
        _events.Add(new ThrowEvent(t + 6,
            $"{attacker.DisplayName()} bitirmeye gitti ama {defender.DisplayName()} savunmayı tuttu. Throw!", defender, 1 - probability));
        return null;
    }
}
