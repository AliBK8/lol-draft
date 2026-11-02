using LolDraft.Engine.Events;

namespace LolDraft.Engine.Simulation;

/// <summary>Kuşatma ve throw (DESIGN.md §16.10): altında önde olan ve kapıya dayanan takım maçı erken bitirmeye gider.</summary>
internal sealed partial class MatchRun
{
    private const int SiegeMinMinute = 20;
    private const int SiegeCooldownMinutes = 3;
    private const int SiegeSecond = 45;
    private const int SiegeMaxKills = 4;
    private const int ThrowMaxKills = 3;

    // Kuşatma şartı: takım altın farkı ≥ 4.000 (Baron buff'ı varken ≥ 2.000) ve bir koridorda dış + iç kule yıkılmış.
    private const double SiegeGoldLead = 4000;
    private const double SiegeGoldLeadWithBaron = 2000;
    private const int SiegeTowersInLane = 2;

    // Buff'lar (DESIGN.md §16.7): Baron 3 dk kuşatma d +4 ve eşik yarıya; Elder 2 dk tüm takım savaşlarında d +6.
    private const double BaronBuffSeconds = 180;
    private const double BaronSiegeBonus = 4;
    private const double ElderBuffSeconds = 120;
    private const double ElderFightBonus = 6;

    // -9: maç başında "son kuşatma çok önceydi" (JS: lastSiege = -9).
    private int _lastSiegeMinute = -9;

    // [taraf]: buff'ın bittiği oyun saniyesi (yoksa 0).
    private readonly double[] _baronUntil = new double[2];
    private readonly double[] _elderUntil = new double[2];

    private bool HasBaron(TeamSide side, double t) => _baronUntil[(int)side] > t;

    private bool HasElder(TeamSide side, double t) => _elderUntil[(int)side] > t;

    private void GiveBuff(TeamSide side, Objective objective, double t)
    {
        if (objective == Objective.Baron)
            _baronUntil[(int)side] = t + BaronBuffSeconds;
        else if (objective == Objective.Elder)
            _elderUntil[(int)side] = t + ElderBuffSeconds;
    }

    // 20. dk'dan sonra objektifsiz her dakika 45. saniyeye kaydolur; şartlar o saniyedeki altına ve kulelere bakar.
    private void ScheduleSiege(int minute)
    {
        if (minute >= SiegeMinMinute && !_objectiveAt.ContainsKey(minute))
            At((minute - 1) * 60 + SiegeSecond, () => PlaySiege(minute));
    }

    // Zar sırası: başarı, sonra kill sayısı (kill'ler kendi saniyelerinde).
    private void PlaySiege(int minute)
    {
        var t = (minute - 1) * 60 + SiegeSecond;
        var lead = TeamGold(TeamSide.Blue, t) - TeamGold(TeamSide.Red, t);
        var attacker = lead > 0 ? TeamSide.Blue : TeamSide.Red;
        var defender = attacker.Opponent();
        var needed = HasBaron(attacker, t) ? SiegeGoldLeadWithBaron : SiegeGoldLead;

        if (Math.Abs(lead) < needed || !AtTheGates(attacker) || minute - _lastSiegeMinute < SiegeCooldownMinutes)
            return;

        _lastSiegeMinute = minute;
        // Başarı ihtimali = takım savaşı ihtimali (Baron buff'ı kuşatmada +4, K6 poke), önde olanın tarafından.
        var probability = FightProbability(attacker, minute, t, FightKind.Siege, report: RuleContext.Siege);

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

        // Throw: savunan kuşatmayı tutar ve kill'leri alır. Önde olanları keser, ödüllerini toplar: fark kendiliğinden
        // kapanır (eskiden sabit +10 puan). Maç sürer.
        ScheduleFightKills(defender, t, ThrowMaxKills);
        At(t + 6, () => _events.Add(new ThrowEvent(t + 6,
            $"{attacker.DisplayName()} bitirmeye gitti ama {defender.DisplayName()} savunmayı tuttu. Throw!", defender, 1 - probability)));
    }

    // Kapıya dayanmak: en az bir koridorda dış ve iç kule yıkılmış.
    private bool AtTheGates(TeamSide side) =>
        Lanes.Any(lane => _towersDestroyed[(int)side, (int)lane] >= SiegeTowersInLane);
}
