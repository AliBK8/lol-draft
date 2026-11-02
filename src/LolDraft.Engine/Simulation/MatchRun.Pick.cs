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

    // Yakalanma ihtimali: p = min(0.03 + 0.01 × takım altın farkı / 1000, 0.12) × (1 + 0.15 × rakip pick toplamı)
    // Örnek: 3.000 önde → her dakika %6; 9.000+ önde → %12 (tavan). Rakipte pick toplamı 4 → ×1,6: %9,6 / %19,2.
    private const double CatchBaseChance = 0.03;
    private const double CatchChancePerThousand = 0.01;
    private const double CatchMaxChance = 0.12;
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

        var t = (minute - 1) * 60 + 5 + _rng.NextDouble() * 35;
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
        if (_rng.NextDouble() >= CatchChance(Math.Abs(lead), pickTotal) || _rng.NextDouble() < EscapeChance(victim))
            return;

        // Katil: rakibin müsait oyuncuları içinde en yüksek pick'li; eşitlikte nehre yakınlık sırası (sıralama kararlı).
        var killer = SkirmishPriority(Team(behind)).Where(c => IsInLane(c, t))
            .OrderByDescending(c => c.Tags.Pick)
            .FirstOrDefault();
        if (killer is null)
            return;

        Kill(behind, killer, victim, t, $"{victim.Name} {CatchPlace(ahead, victim)} yakalandı, kill {killer.Name} aldı");
    }

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
