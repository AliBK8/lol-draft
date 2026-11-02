using LolDraft.Engine.Events;

namespace LolDraft.Engine.Simulation;

/// <summary>Kuşatma ve throw (DESIGN.md §16.10): altında önde olan ve kapıya dayanan takım maçı erken bitirmeye gider.</summary>
internal sealed partial class MatchRun
{
    private const int SiegeMinMinute = 16;
    private const int SiegeCooldownMinutes = 3;
    private const int SiegeSecond = 45;
    private const int SiegeMaxKills = 4;
    private const int ThrowMaxKills = 3;

    // Kuşatma şartı: takım altın farkı ≥ eşik (Baron buff'ı varken yarısı) ve bir koridorda dış + iç kule yıkılmış.
    // Eşik dk 20'den önce daha büyük: 16. dk 7.000 → dakikada −750 → 20. dk'dan sonra 4.000. Çok önde olan erken
    // bitirebilir, az önde olan 20'yi bekler. Eskiden kuşatma 20. dk'da açılıyordu: önde olanlar birikip maçların %12'si
    // tam 20. dk'da bitiyordu (DESIGN.md faz D).
    private const int SiegeFullMinute = 20;
    private const double SiegeGoldLead = 4000;
    private const double SiegeEarlyLeadPerMinute = 750;
    private const int SiegeTowersInLane = 2;

    // Savunanın ev sahibi avantajı: kule ve inhibitör altında savaşır, saldıranın d'si −3. Sadece önde olanı etkiler
    // (kuşatmaya önde olan gider); tutulursa throw: savunan kill ve ödül alır, fark kapanır. Faz D: önde olan farkı
    // fazla kolay maça çeviriyordu (15. dk 5.000+ önde %82, hedef ~%75).
    private const double SiegeDefenseBonus = 3;

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

    // 16. dk'dan sonra objektifsiz her dakika 45. saniyeye kaydolur; şartlar o saniyedeki altına ve kulelere bakar.
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
        var needed = SiegeLeadNeeded(minute) / (HasBaron(attacker, t) ? 2 : 1);

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

    // Baron'suz eşik: 16. dk 7.000, 17. 6.250, 18. 5.500, 19. 4.750, 20+ 4.000.
    internal static double SiegeLeadNeeded(int minute) =>
        SiegeGoldLead + SiegeEarlyLeadPerMinute * Math.Max(0, SiegeFullMinute - minute);

    // Kapıya dayanmak: en az bir koridorda dış ve iç kule yıkılmış.
    private bool AtTheGates(TeamSide side) =>
        Lanes.Any(lane => _towersDestroyed[(int)side, (int)lane] >= SiegeTowersInLane);
}
