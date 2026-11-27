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

    // Konumlu yakalanma (DESIGN.md §4.11 D): ortak avcı %50, her yardımcı %50; savaş nehir çatışmasının hesabıyla
    // (bölen 6), en fazla 2 kill. Katılanlar 12 sn önce yere yürür, 4 sn kalır.
    private const double CatchPartnerChance = 0.5;
    private const double CatchAnswerChance = 0.5;
    private const double CatchFightDivisor = 6;
    private const int CatchMaxKills = 2;
    private const int CatchMoveLead = 12;
    private const int CatchMoveTail = 4;

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

        PlayLocatedCatch(behind, killer, victim, t);
    }

    // Konumlu yakalanma (DESIGN.md §4.11 D). Zar sırası: ortak avcı gelir mi, (yer belirsizse) orman yarısı, her aday
    // yardımcı için gelir mi, (savaş olursa) kazanan ve kill sayısı.
    //   Yer: kurban koridorunda takım arkadaşlarıyla birlikteyse o koridorda; yalnızsa (1-4'ün splitçisi, 1-3-1'in
    //   yanları) ya da ormancıysa avcının ormanında (koridoruna göre üst ya da alt yarı): dönerken / ward atarken avlanır.
    //   Yardım: koridorda yakalanana o koridordaki takım arkadaşları, ormanda yakalanana kendi ormancısı, her biri %50.
    //   Yardım gelirse küçük savaş (nehir çatışmasının hesabı, bölen 6, K7): kazanan kaybedenlerden 1-2 kill alır.
    // Örnek: Fiora 1-4'te üstte tek; Kırmızı ormanında (kuşlar) Elise ve Lee Sin onu yakalar, yardıma gelen yok → kill.
    // Örnek: Jinx mid'de grupla; yakalanırken Lulu ve Ornn yardıma gelir → koridorda 2v3, avcılar zor durumda.
    private void PlayLocatedCatch(TeamSide hunter, ChampionProfile killer, ChampionProfile victim, double t)
    {
        var prey = hunter.Opponent();
        var partner = SkirmishPriority(Team(hunter)).FirstOrDefault(c => c != killer && IsInLane(c, t));
        var withPartner = partner is not null && CatchRng.NextDouble() < CatchPartnerChance;
        ChampionProfile[] hunters = withPartner ? [killer, partner!] : [killer];

        var spot = CatchSpot(hunter, victim, t, half: null);
        var helpers = CatchHelpers(prey, victim, spot, t);
        var place = Place(spot);

        if (helpers.Count == 0)
        {
            _events.Add(new CatchEvent(t, "", hunter, victim.Id, [.. hunters.Select(c => c.Id)], [], spot.Lane, spot.Jungle,
                spot.Camp, Probability: null, CatchMoveLead, CatchMoveTail));
            MarkAway(hunters, t + AwayAfterRoamSeconds);
            Kill(hunter, killer, victim, t, $"{victim.Name} {place} yakalandı, kill {killer.Name} aldı",
                assists: withPartner ? [partner!] : []);
            return;
        }

        // Yardım geldi: küçük savaş.
        ChampionProfile[] defenders = [victim, .. helpers];
        var minute = (int)(t / 60) + 1;
        var (blue, red) = hunter == TeamSide.Blue ? (hunters, defenders) : (defenders, hunters);
        var rules = TeamRules.Effects(FightKind.Skirmish, blue, red, minute, c => EffectivePower(c, minute, t));
        var d = EffectivePower(blue, minute, t) - EffectivePower(red, minute, t) + rules.Sum(e => e.Signed);
        var blueProbability = Probability.Clamp(Probability.Logistic(d / CatchFightDivisor));
        var hunterProbability = hunter == TeamSide.Blue ? blueProbability : 1 - blueProbability;
        var winner = CatchRng.NextDouble() < blueProbability ? TeamSide.Blue : TeamSide.Red;
        var (winners, losers) = winner == hunter ? (hunters, defenders) : (defenders, hunters);

        var names = string.Join(", ", helpers.Select(c => c.Name));
        var text = $"{victim.Name} {place} yakalanmak üzereydi, {names} yardıma geldi: "
                   + $"{blue.Length}v{red.Length}, {winner.DisplayName()} kazandı";
        _events.Add(new CatchEvent(t, text, hunter, victim.Id, [.. hunters.Select(c => c.Id)], [.. helpers.Select(c => c.Id)],
            spot.Lane, spot.Jungle, spot.Camp, hunterProbability, CatchMoveLead, CatchMoveTail));
        MarkAway([.. hunters, .. defenders], t + AwayAfterFightSeconds);

        var killCount = 1 + CatchRng.NextIndex(Math.Min(CatchMaxKills, losers.Length));
        ScheduleKills(CatchRng, winner, winners, losers, killCount, t + 1,
            (k, v) => $"{k.Name}, {v.Name} karakterini {place} indirdi");
    }

    private sealed record CatchLocation(Lane? Lane, TeamSide? Jungle, JungleCamp? Camp);

    // Yer: kurban koridorunda müsait bir takım arkadaşıyla birlikteyse o koridor; değilse avcının ormanı. Orman yarısı
    // koridordan (üst → üst yarı, alt → alt yarı); mid'de ya da ormancıysa zarla.
    private CatchLocation CatchSpot(TeamSide hunter, ChampionProfile victim, double t, bool? half)
    {
        var prey = hunter.Opponent();
        var post = Post(victim);
        var together = post is { } lane
                       && Team(prey).Champions.Any(c => c != victim && Post(c) == lane && IsInLane(c, t));
        if (together)
            return new CatchLocation(post, null, null);

        var top = half ?? post switch
        {
            Lane.Top => true,
            Lane.Bot => false,
            _ => CatchRng.NextIndex(2) == 0,
        };
        return new CatchLocation(null, hunter, JungleHalfCamp(hunter, top));
    }

    // Bir takımın ormanının üst / alt yarısındaki istila kampı (harita: Mavi'nin üst yarısında kurtlar, alt yarısında
    // kuşlar; Kırmızı'da tersi). Ekran savaşı o kampta gösterir.
    private static JungleCamp JungleHalfCamp(TeamSide owner, bool top) =>
        (owner == TeamSide.Blue) == top ? JungleCamp.Wolves : JungleCamp.Raptors;

    // Yardıma gelebilecekler: koridorda yakalanana o koridordaki müsait takım arkadaşları, ormanda yakalanana müsait
    // ormancısı (kurban kendisi değilse). Her biri %50 (zar takım sırasıyla, sadece aday varsa).
    private List<ChampionProfile> CatchHelpers(TeamSide prey, ChampionProfile victim, CatchLocation spot, double t)
    {
        var team = Team(prey);
        var candidates = spot.Lane is { } lane
            ? team.Champions.Where(c => c != victim && Post(c) == lane && IsInLane(c, t)).ToList()
            : team.Champions.Where(c => c == team.Jungle && c != victim && IsInLane(c, t)).ToList();
        var helpers = new List<ChampionProfile>();
        foreach (var candidate in candidates)
        {
            if (CatchRng.NextDouble() < CatchAnswerChance)
                helpers.Add(candidate);
        }

        return helpers;
    }

    private string Place(CatchLocation spot) =>
        spot.Lane is { } lane ? $"{LaneName(lane)} koridorda" : $"{spot.Jungle!.Value.DisplayName()} ormanında";

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

        // Yer (DESIGN.md §4.11 D): avcının ormanında, objektifin yarısında (ejder/Elder alt, Herald/Baron üst; son savaş
        // zarla). Yardım yok: objektife koşarken pusuya düşer (ayarı faz H3'te, cevapsız).
        bool? half = objective switch
        {
            Objective.Dragon or Objective.Elder => false,
            Objective.Herald or Objective.Baron => true,
            _ => null,
        };
        var top = half ?? CatchRng.NextIndex(2) == 0;
        var spot = new CatchLocation(null, hunter, JungleHalfCamp(hunter, top));
        _events.Add(new CatchEvent(t, "", hunter, victim.Id, [killer.Id], [], null, hunter, spot.Camp, Probability: null,
            CatchMoveLead, CatchMoveTail));
        Kill(hunter, killer, victim, t,
            $"{BeforeObjective(objective)} öncesi pusu: {victim.Name} {Place(spot)} yakalandı, kill {killer.Name} aldı");
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
}
