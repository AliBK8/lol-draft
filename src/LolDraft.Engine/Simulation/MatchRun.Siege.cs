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

    // 20. dk'dan sonra objektifsiz her dakika 45. saniyeye kaydolur; şartlar o saniyedeki puana bakar.
    private void ScheduleSiege(int minute)
    {
        if (minute >= SiegeMinMinute && !_objectiveAt.ContainsKey(minute))
            At((minute - 1) * 60 + SiegeSecond, () => PlaySiege(minute));
    }

    // Kuşatma şartları: |toplam| ≥ 45 ve son kuşatmadan bu yana ≥ 3 dk.
    // Zar sırası: başarı, sonra kill sayısı (kill'ler kendi saniyelerinde).
    private void PlaySiege(int minute)
    {
        var total = _scores.Total;
        if (Math.Abs(total) < SiegeThreshold || minute - _lastSiegeMinute < SiegeCooldownMinutes)
            return;

        _lastSiegeMinute = minute;
        var attacker = total > 0 ? TeamSide.Blue : TeamSide.Red;
        var defender = attacker.Opponent();
        var t = (minute - 1) * 60 + SiegeSecond;
        // Başarı ihtimali = takım savaşı ihtimali, önde olanın tarafından.
        var probability = FightProbability(attacker, minute);

        _events.Add(new SiegeEvent(t, "", attacker, Lead: 20, Tail: 4));
        MarkAway([.. AliveAt(attacker, t), .. AliveAt(defender, t)], t + AwayAfterFightSeconds);

        if (_rng.NextDouble() < probability)
        {
            ScheduleFightKills(attacker, t, SiegeMaxKills);
            At(t + 8, () =>
            {
                _events.Add(new NexusEvent(t + 8,
                    $"{attacker.DisplayName()} kuşatmayı kazandı ve Nexus'u yıktı, maç {minute}. dakikada bitti", attacker, probability));
                EndMatch(new MatchEnd(minute, attacker, MatchEndKind.Siege, probability));
            });
            return;
        }

        // Throw: savunan kuşatmayı tutar, +10 puan ve kill'ler alır. Önde olanın avantajı azalır, maç sürer.
        _scores.Add(ScoreCategory.Siege, defender.Sign() * ThrowValue);
        ScheduleFightKills(defender, t, ThrowMaxKills);
        At(t + 6, () => _events.Add(new ThrowEvent(t + 6,
            $"{attacker.DisplayName()} bitirmeye gitti ama {defender.DisplayName()} savunmayı tuttu. Throw!", defender, 1 - probability)));
    }
}
