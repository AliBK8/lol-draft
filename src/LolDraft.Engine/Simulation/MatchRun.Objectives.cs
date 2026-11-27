using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Objektif savaşları, ejder ruhu ve dk 35 son savaş (DESIGN.md §4.5 Objektifler).</summary>
internal sealed partial class MatchRun
{
    private const int FinalMinute = 35;
    private const int ObjectiveSecond = 30;

    // Takım savaşı ayarları (DESIGN.md §4.5).
    private const double FightDivisor = 15;

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

    // Bu dakika objektif varsa 30. saniyeye kaydolur; ondan 8-25 sn önce objektif öncesi yakalama (MatchRun.Pick.cs).
    // Zar sırası: yakalamanın saniyesi kayıtta.
    private void ScheduleObjective(int minute)
    {
        if (!_objectiveAt.TryGetValue(minute, out var objective))
            return;

        var fightT = (minute - 1) * 60 + ObjectiveSecond;
        var catchT = fightT - PreCatchLeadSeconds(ObjectiveRng.NextDouble());
        At(catchT, () => PlayPreObjectiveCatch(catchT, objective));
        At(fightT, () => PlayObjective(minute, objective));
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

    // Zar sırası: split (gidiyor mu, cevap veriyor mu), kazanan, kill sayısı (kill'ler kendi saniyelerinde).
    // Son savaşsa Nexus'u kaydeder.
    private void PlayObjective(int minute, Objective objective)
    {
        var t = (minute - 1) * 60 + ObjectiveSecond;
        var kind = objective == Objective.FinalFight ? FightKind.Final : FightKind.Objective;

        // Split (K12): splitter (ve cevap veren) savaşta yok; savaş 4v5 ya da 4v4.
        var split = PlanSplit(minute, objective, t);
        ChampionProfile[] absent = split?.Absent ?? [];
        if (split is not null)
            PlaySplit(split, minute, t);

        // Arkadan kapatma (DESIGN.md §4.11 D): çukurun öbür ucundaki yan koridordan gelenler.
        var blueFlank = Flankers(TeamSide.Blue, objective, minute, t, absent);
        var redFlank = Flankers(TeamSide.Red, objective, minute, t, absent);
        foreach (var (side, flankers) in ((TeamSide, List<ChampionProfile>)[])[(TeamSide.Blue, blueFlank), (TeamSide.Red, redFlank)])
        {
            if (flankers.Count > 0)
                _events.Add(new FlankEvent(t, "", side, objective, [.. flankers.Select(c => c.Id)]));
        }

        var blueProbability = FightProbability(TeamSide.Blue, minute, t, kind, absent,
            report: kind == FightKind.Final ? RuleContext.FinalFight : RuleContext.Objective, objective: objective,
            flank: FlankBonus * (blueFlank.Count - redFlank.Count));
        var winner = ObjectiveRng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var winnerProbability = winner == TeamSide.Blue ? blueProbability : 1 - blueProbability;

        // Geri dönüşün ayrı çarpanı yok (eskiden ×1.25): geride olan kazanırsa önde olanları keser, ödülü toplar (§16.4).
        _scores.Add(Category(objective), winner.Sign() * Value(objective));

        var text = objective == Objective.FinalFight
            ? $"Son takım savaşını {winner.DisplayName()} kazandı"
            : $"{winner.DisplayName()} {Accusative(objective)} aldı";
        _events.Add(new ObjectiveEvent(t, text, objective, winner, winnerProbability));
        GiveObjectiveGold(winner, objective);
        GiveBuff(winner, objective, t);
        if (objective is Objective.Dragon or Objective.Elder)
            AfterDragonOrElder(objective, winner, minute, t);

        // Savaşa iki takımın yaşayanları gelir (split'tekiler hariç); bir süre koridorlarında değiller.
        var losers = AliveAt(winner.Opponent(), t).Except(absent).ToList();
        MarkAway([.. AliveAt(winner, t).Except(absent), .. losers], t + AwayAfterFightSeconds);
        var winnerFlank = winner == TeamSide.Blue ? blueFlank : redFlank;
        ScheduleFightKills(ObjectiveRng, winner, t, MaxKills(objective), absent, opener: winnerFlank.FirstOrDefault());

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
    // absent: savaşta olmayanlar (split).
    // opener: arkadan dolanan (DESIGN.md §4.11 D); varsa ilk kill onun, kurban kaybedenlerin en zengini (carry).
    private void ScheduleFightKills(IRandomSource rng, TeamSide winner, double t, int maxKills,
        IReadOnlyCollection<ChampionProfile>? absent = null, ChampionProfile? opener = null)
    {
        var winners = AliveAt(winner, t).Except(absent ?? []).ToList();
        var losers = AliveAt(winner.Opponent(), t).Except(absent ?? []).ToList();
        var killCount = Math.Min(losers.Count, 1 + rng.NextIndex(maxKills));
        var firstT = t + 2;

        if (opener is not null && killCount > 0)
        {
            // Değer kopyası: firstT aşağıda ilerliyor, lambda değişkeni değil anı yakalasın.
            var openT = firstT;
            At(openT, () =>
            {
                var victim = losers.Where(c => _deaths.IsAlive(c, openT)).MaxBy(c => _gold.GoldAt(c, openT));
                if (victim is null || !_deaths.IsAlive(opener, openT))
                    return;
                Kill(winner, opener, victim, openT, $"{opener.Name} arkadan dolandı, {victim.Name} düştü",
                    assists: winners.Where(c => c != opener && _deaths.IsAlive(c, openT)).ToList());
            });
            killCount--;
            firstT += KillInterval;
        }

        ScheduleKills(rng, winner, winners, losers, killCount, firstT,
            (killer, victim) => $"Takım savaşında {victim.Name} düştü, kill {killer.Name} aldı");
    }

    // Arkadan kapatma (DESIGN.md §4.11 D, basit hâli; asıl hâli F3 mini savaşta): objektif savaşına çukurun öbür ucundaki
    // yan koridordan (ejder/Elder: üst; Herald/Baron: alt) gelen, savaşa geç ama rakibin arkasından katılır. Kişi başı
    // savaş d'sine +1 (takım başı en fazla 2 kişi); takımı kazanırsa ilk kill onun, kurban rakibin en zengini.
    // Bedeli: savaşa kadar o koridorda durur (düzen onu oraya koymuş: grup bir kişi eksik dizilir). Son savaşta ve
    // 14. dakikadan önce yok.
    // Örnek: 1-4'te Fiora üstte, ejder savaşı: Mavi d +1; Mavi kazanırsa "Fiora arkadan dolandı, Jinx düştü".
    private const double FlankBonus = 1;
    private const int MaxFlankers = 2;

    private List<ChampionProfile> Flankers(TeamSide side, Objective objective, int minute, double t,
        IReadOnlyCollection<ChampionProfile> absent)
    {
        // Düzenler gibi 14. dakikadan sonra (lane fazında herkes kendi koridorunda, top ejdere gelmez sayılmaz).
        if (minute < LayoutMinMinute)
            return [];
        Lane? far = objective switch
        {
            Objective.Dragon or Objective.Elder => Lane.Top,
            Objective.Herald or Objective.Baron => Lane.Bot,
            _ => null,
        };
        if (far is null)
            return [];
        return [.. Team(side).Champions.Where(c => Post(c) == far && _deaths.IsAlive(c, t) && !absent.Contains(c)).Take(MaxFlankers)];
    }

    // Takım savaşı ihtimali (DESIGN.md §16.7):
    //   d      = (ΣG_eff Mavi − ΣG_eff Kırmızı) + takım kuralları (savaş türüne göre) + ruh (±2) + Elder buff'ı (±6)
    //            + kuşatmada Baron buff'ı (±4)
    //   p_Mavi = clamp(1 / (1 + e^(−d/15)), 0.1, 0.9)
    // Bölen 15 eğrinin dikliği: d = 15 → ~%73. Altın üstünlüğü G_eff'in içinde; sonucu zar belirler.
    // Kurallar (K5-K11, K13) draft'ın kimliği: engage objektifte, poke kuşatmada, korunan taşıyıcı 25. dk'dan sonra...
    // Kırmızı için d'nin işareti çevrilir: p_Kırmızı = 1 − p_Mavi (clamp simetrik olduğu için aynı kalır).
    // Savaşta sadece o an yaşayanlar var: ölüler ve absent (split) ne güçleriyle ne etiketleriyle savaşa girer.
    // Faz H3: eskiden ölüler de sayılıyordu; objektif öncesi yakalanan oyuncu savaşı 5'e 4 yapmıyordu.
    // report: gerçek savaşsa karneye yazılacağı yer (split kararındaki "savaş olsa" hesabı yazılmaz).
    // objective: objektif savaşıysa hangisi (oyun planının ejder ve Herald düğmeleri için).
    private double FightProbability(TeamSide side, int minute, double t, FightKind kind,
        IReadOnlyCollection<ChampionProfile>? absent = null, RuleContext? report = null, Objective? objective = null,
        double flank = 0)
    {
        var siege = kind == FightKind.Siege;
        var soulBonus = _soul is { } soul ? soul.Sign() * SoulFightBonus : 0;
        var blue = AliveAt(TeamSide.Blue, t).Except(absent ?? []).ToList();
        var red = AliveAt(TeamSide.Red, t).Except(absent ?? []).ToList();

        // Bir tarafta savaşacak kimse yoksa (hepsi ölü ya da split'te) kurallar hesaplanmaz, diğer taraf tavanla
        // kazanır (%90, ihtimaller %10-%90). Eskiden kurallar boş takımın ortalamasını almaya çalışıp çöküyordu
        // (savunanın beşi de ölüyken kuşatma).
        if (blue.Count == 0 || red.Count == 0)
        {
            var blueWins = blue.Count == red.Count ? 0.5 : red.Count == 0 ? Probability.Max : Probability.Min;
            return side == TeamSide.Blue ? blueWins : 1 - blueWins;
        }

        var rules = TeamRules.Effects(kind, blue, red, minute, c => EffectivePower(c, minute, t));
        // Kurallar net toplamda tavanlı (TeamRules.CappedTotal): uç draft'ların cezaları sonsuz birikmesin.
        var ruleSum = rules.Sum(e => e.Signed);
        var d = (EffectivePower(blue, minute, t) - EffectivePower(red, minute, t)) + TeamRules.CappedTotal(ruleSum)
                + soulBonus + BuffBonus(TeamSide.Blue, t, siege) - BuffBonus(TeamSide.Red, t, siege);
        // Oyun planı (Tactics.cs): Ejder & late ejder/Elder'da +4,5, Herald'da −4,5 (uyumla ölçekli).
        // Sıfırsa eklenmez (Dengeli bit bit aynı).
        var plan = PlanFightBonus(TeamSide.Blue, kind, objective) - PlanFightBonus(TeamSide.Red, kind, objective);
        if (plan != 0)
            d += plan;
        // Arkadan kapatma (Mavi'nin gözünden, kişi başı ±1; MatchRun.Objectives Flankers). Sıfırsa eklenmez.
        if (flank != 0)
            d += flank;
        // Kuşatmada side = saldıran; savunan kule altında savaşır (MatchRun.Siege.cs). Hızlı bitir planıyla saldıranın
        // kuşatması daha zor tutulur (savunana +3, uyumla ölçekli).
        if (siege)
        {
            d -= side.Sign() * SiegeDefenseBonus;
            if (Tactics(side).SiegeDefenseExtra > 0)
                d -= side.Sign() * Tactics(side).SiegeDefenseExtra;
        }
        if (report is { } context)
            ReportFightRules(rules, context, d, ruleSum, FightDivisor);
        return Probability.Clamp(Probability.Logistic(side.Sign() * d / FightDivisor));
    }

    // Bir takımın planından bu savaşa gelen d (kendi gözünden).
    private double PlanFightBonus(TeamSide side, FightKind kind, Objective? objective)
    {
        var tactics = Tactics(side);
        var bonus = 0.0;
        if (objective is Objective.Dragon or Objective.Elder)
            bonus += tactics.DragonFightBonus;
        if (objective is Objective.Herald)
            bonus -= tactics.HeraldFightPenalty;
        if (kind is FightKind.Objective or FightKind.Final)
            bonus -= tactics.TeamFightPenalty;
        return bonus;
    }

    private double BuffBonus(TeamSide side, double t, bool siege) =>
        (HasElder(side, t) ? ElderFightBonus : 0) + (siege && HasBaron(side, t) ? BaronSiegeBonus : 0);

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
