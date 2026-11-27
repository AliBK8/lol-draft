using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Yakalanma (DESIGN.md §16.9): çok önde olan takım bazen bir yerde yakalanır. Önde olan için risk, geride olan için
/// umut; katil kurbanın shutdown ödülünü alır ve fark kapanır.
/// </summary>
internal sealed partial class MatchRun
{
    private const int CatchMinMinute = 15;
    private const double CatchMinTeamLead = 1000;

    // Yakalanma ihtimali: p = min(0.03 + 0.02 × takım altın farkı / 1000, 0.2) × (1 + 0.15 × rakip pick toplamı)
    // Örnek: 3.000 önde → her dakika %9; 8.500+ önde → %20 (tavan). Rakipte pick toplamı 4 → ×1,6: %14,4 / %32.
    // Faz D: eskiden 1.000 başına %1, tavan %12. Büyük farkı köreltir, draft tarzına bakmaz (15. dk 5.000+ önde
    // olanın kazanma oranını indirmek için).
    private const double CatchBaseChance = 0.03;
    private const double CatchChancePerThousand = 0.02;
    private const double CatchMaxChance = 0.2;
    private const double CatchChancePerPick = 0.15;

    // Kaçma şansı: 0.2 + 0.1 × mob. Örnek: Draven (mob 1) %30, LeBlanc (mob 3) %50.
    private const double EscapeBaseChance = 0.2;
    private const double EscapeChancePerMobility = 0.1;

    // dk ≥ 15, objektif olmayan dakikalarda (objektifte herkes savaşta), 5-40. saniyede (45. sn kuşatmadan önce).
    // Zar sırası: kayıtta saniye; çözümde yakalanma, kaçma.
    private void ScheduleCatch(int minute)
    {
        if (minute < CatchMinMinute || _objectiveAt.ContainsKey(minute))
            return;

        var t = (minute - 1) * 60 + 5 + CatchRng.NextDouble() * 35;
        At(t, () => PlayCatch(t));
    }

    private void PlayCatch(double t)
    {
        var lead = TeamGold(TeamSide.Blue, t) - TeamGold(TeamSide.Red, t);
        if (Math.Abs(lead) < CatchMinTeamLead)
            return;

        var ahead = lead > 0 ? TeamSide.Blue : TeamSide.Red;
        // Kurban: önde olan takımın, koridorunda olan (ölü ya da savaşta değil) en zengini: rol rakibine göre en önde.
        var victim = Team(ahead).Champions.Where(c => IsInLane(c, t))
            .OrderByDescending(c => _gold.GoldAt(c, t) / _gold.GoldAt(Counterpart(c), t))
            .FirstOrDefault();
        if (victim is null)
            return;

        var behind = ahead.Opponent();
        var pickTotal = Team(behind).Champions.Sum(c => c.Tags.Pick);
        if (CatchRng.NextDouble() >= CatchChance(Math.Abs(lead), pickTotal) || CatchRng.NextDouble() < EscapeChance(victim))
            return;

        // Katil: rakibin müsait oyuncuları içinde en yüksek pick'li; eşitlikte nehre yakınlık sırası (sıralama kararlı).
        var killer = SkirmishPriority(Team(behind)).Where(c => IsInLane(c, t))
            .OrderByDescending(c => c.Tags.Pick)
            .FirstOrDefault();
        if (killer is null)
            return;

        Kill(behind, killer, victim, t, $"{victim.Name} {CatchPlace(ahead, victim)} yakalandı, kill {killer.Name} aldı");
    }

    // Objektif öncesi yakalama (faz H3, kullanıcıyla): draft'ın pick gücü objektife taşınır. Objektif savaşından
    // 8-25 sn önce (rastgele) pick toplamı fazla olan takım rakibinden birini yakalamayı dener:
    //   p = min(%4 × pick farkı, %25) · kurban: rakibin koridorundakilerden rastgele · kaçma: 0,2 + 0,1 × mob
    // Yeniden doğma 20-45 sn: yakalanan savaşa yetişmez, savaş 5'e 4 (savaşa sadece yaşayanlar girer).
    // Örnek: pick 9 vs 4 → %20; Baron'dan 15 sn önce adc yakalanırsa Baron savaşı 5'e 4.
    // Mevcut yakalanmadan (önde olan yakalanır, geri dönüş) ayrı: bu, önde/geride bakmaz, draft'tan gelir.
    private const double PreCatchChancePerPick = 0.04;
    private const double PreCatchMaxChance = 0.25;
    private const double PreCatchMinLead = 8;
    private const double PreCatchMaxLead = 25;

    private static double PreCatchLeadSeconds(double roll) => PreCatchMinLead + roll * (PreCatchMaxLead - PreCatchMinLead);

    internal static double PreObjectiveCatchChance(int pickDifference) =>
        Math.Min(PreCatchChancePerPick * Math.Max(0, pickDifference), PreCatchMaxChance);

    // Zar sırası: yakalama, kurban, kaçma (sadece şartlar tutunca: fark var, iki tarafta da müsait biri var).
    private void PlayPreObjectiveCatch(double t, Objective objective)
    {
        var diff = Team(TeamSide.Blue).Champions.Sum(c => c.Tags.Pick) - Team(TeamSide.Red).Champions.Sum(c => c.Tags.Pick);
        if (diff == 0)
            return;

        var hunter = diff > 0 ? TeamSide.Blue : TeamSide.Red;
        var prey = hunter.Opponent();
        var victims = Team(prey).Champions.Where(c => IsInLane(c, t)).ToList();
        var killer = SkirmishPriority(Team(hunter)).Where(c => IsInLane(c, t))
            .OrderByDescending(c => c.Tags.Pick)
            .FirstOrDefault();
        if (victims.Count == 0 || killer is null)
            return;

        if (CatchRng.NextDouble() >= PreObjectiveCatchChance(Math.Abs(diff)))
            return;
        var victim = victims[CatchRng.NextIndex(victims.Count)];
        if (CatchRng.NextDouble() < EscapeChance(victim))
            return;

        Kill(hunter, killer, victim, t,
            $"{BeforeObjective(objective)} öncesi pusu: {victim.Name} {CatchPlace(prey, victim)} yakalandı, kill {killer.Name} aldı");
    }

    private static string BeforeObjective(Objective objective) => objective switch
    {
        Objective.Dragon => "Ejder",
        Objective.Herald => "Herald",
        Objective.Baron => "Baron",
        Objective.Elder => "Elder",
        Objective.FinalFight => "Son savaş",
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };

    internal static double CatchChance(double teamLead, int opponentPickTotal) =>
        Math.Min(CatchBaseChance + CatchChancePerThousand * teamLead / 1000, CatchMaxChance)
        * (1 + CatchChancePerPick * opponentPickTotal);

    internal static double EscapeChance(ChampionProfile victim) =>
        EscapeBaseChance + EscapeChancePerMobility * victim.Tags.Mobility;

    private double TeamGold(TeamSide side, double t)
    {
        var total = 0.0;
        foreach (var champion in Team(side).Champions)
            total += _gold.GoldAt(champion, t);
        return total;
    }

    private string CatchPlace(TeamSide side, ChampionProfile champion)
    {
        var team = Team(side);
        if (champion == team.Jungle)
            return "ormanda";
        var lane = champion == team.Top ? Lane.Top : champion == team.Mid ? Lane.Mid : Lane.Bot;
        return $"{LaneName(lane)} koridorda";
    }
}
