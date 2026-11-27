using LolDraft.Engine.Events;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Kule baskısı (DESIGN.md §4.11 C): anlatıcı dakikasında koridordaki kişi sayıları (harita düzenine göre, müsait
/// olanlar: ölü ya da nehir çatışması, objektif, kuşatma, gank'taki yok) kule ihtimaline döner. Boş koridor ya da sayı
/// üstünlüğü kule getirebilir; eşitlikte sadece K6 poke itişi (TryPokePush). Ayrıca savaş sonrası itiş ve split kulesi.
/// </summary>
internal sealed partial class MatchRun
{
    private const int TowersPerLane = 3;
    private const double TowerValue = 4;
    private const double InhibitorValue = 6;

    // Koridor fırsatı dk ≥ 14'te (plakalar bitince); koridorda önde olmak gerekmez, rakibin yokluğu yeter.
    private const int TowerMinMinute = 14;

    // Meşguliyet: savaşa katılan 25 sn, gank'a giden support 20 sn koridorundan uzak sayılır.
    private const double AwayAfterFightSeconds = 25;
    private const double AwayAfterRoamSeconds = 20;

    // Savaş sonrası itiş: savaştan 12 sn sonra kaybedenlerden en az 2 kişi ölüyse kazanan yakındaki kuleyi alır.
    private const double PostFightPushDelay = 12;
    private const int PostFightMinDead = 2;

    // [taraf, koridor]: o tarafın o koridorda yıktığı kule sayısı (0 dış, 1 iç, 2 inhibitör).
    private readonly int[,] _towersDestroyed = new int[2, Lanes.Length];

    // Kim ne zamana kadar koridorundan uzakta. Sadece arama; üzerinde döngü yok, sıra deterministikliği etkilemez.
    private readonly Dictionary<string, double> _awayUntil = [];

    private void MarkAway(IEnumerable<ChampionProfile> champions, double until)
    {
        foreach (var champion in champions)
            _awayUntil[champion.Id] = Math.Max(until, _awayUntil.GetValueOrDefault(champion.Id));
    }

    // Koridorda mı: yaşıyor ve başka yerde meşgul değil.
    private bool IsInLane(ChampionProfile champion, double t) =>
        _deaths.IsAlive(champion, t) && _awayUntil.GetValueOrDefault(champion.Id) <= t;

    // Kule baskısı (DESIGN.md §4.11 C): koridordaki kişi sayıları (harita düzenine göre, müsait olanlar) kule ihtimaline
    // döner. Karşıda kimse yoksa 1 kişi %35, her ek kişi +%20 (en fazla %85); savunan varsa sadece sayı üstünlüğüyle,
    // fark başına %15 (en fazla %60); eşitlik ya da azınlıkta kule yok. İçerideki kule daha zor: dış ×1, iç ×0,7,
    // inhibitör ×0,5 (kapı yavaş açılır, kuşatma geç gelir).
    // Örnekler: top tek başına boş koridorda dış kule %35, iç %24,5. Grup (5) tek savunana karşı mid'de dış %60, iç %42.
    private const double EmptyLaneTowerChance = 0.2;
    private const double EmptyLaneChancePerExtra = 0.15;
    private const double EmptyLaneMaxChance = 0.6;
    private const double OutnumberChancePerPlayer = 0.1;
    private const double OutnumberMaxChance = 0.4;
    private static readonly double[] TierFactor = [1, 0.5, 0.35];

    internal static double TowerChance(int ours, int theirs, int tier)
    {
        if (ours == 0 || ours <= theirs)
            return 0;
        var chance = theirs == 0
            ? Math.Min(EmptyLaneTowerChance + EmptyLaneChancePerExtra * (ours - 1), EmptyLaneMaxChance)
            : Math.Min(OutnumberChancePerPlayer * (ours - theirs), OutnumberMaxChance);
        return chance * TierFactor[tier];
    }

    // Kuleyi yıkar: sıradaki kule (dış → iç → inhibitör).
    // scored: koridor fırsatı kendi puanını getirir (koridor kategorisine +4, inhibitör +6). Savaş sonrası kule ise
    // kazanılan savaşın görünür sonucudur; puanı objektif değerinde zaten var, ayrıca eklenmez (kartopunu büyütmesin).
    private void TakeTower(TeamSide side, Lane lane, double t, string text, bool scored)
    {
        var tier = _towersDestroyed[(int)side, (int)lane]++;
        if (scored)
            _scores.Add(LaneCategory(lane), side.Sign() * (tier == 2 ? InhibitorValue : TowerValue));
        GiveTowerGold(side, lane);
        _events.Add(new TowerEvent(t, $"{text} {TowerName(tier)} yıktı", side, lane, tier));
    }

    // Anlatıcı anında koridor fırsatı: önce önde olan taraf, sonra diğeri. Kule düştüyse true.
    private bool TryLaneTower(TeamSide ahead, Lane lane, int minute, double t)
    {
        if (minute < TowerMinMinute)
            return false;

        // Önce önde olan taraf, sonra diğeri; zar sadece ihtimal varsa atılır (kule akışı, diğer zarları kaydırmaz).
        foreach (var side in (TeamSide[])[ahead, ahead.Opponent()])
        {
            var tier = _towersDestroyed[(int)side, (int)lane];
            if (tier >= TowersPerLane)
                continue;

            var theirs = PresentIn(side.Opponent(), lane, t).Count;
            var chance = TowerChance(PresentIn(side, lane, t).Count, theirs, tier);
            if (chance > 0 && TowerRng.NextDouble() < chance)
            {
                var how = theirs == 0 ? "" : " sayı üstünlüğüyle";
                TakeTower(side, lane, t, $"{side.DisplayName()}, {LaneName(lane)} koridorda{how}", scored: true);
                return true;
            }
        }

        return TryPokePush(lane, t);
    }

    // K6 poke itişi (TeamRules.PokePushChance): fırsat yoksa poke'u fazla olan taraf, kendinden biri (düzene göre) koridordaysa
    // savunan oradayken de kuleyi alabilir. Zar sadece şartlar tutunca atılır.
    private bool TryPokePush(Lane lane, double t)
    {
        foreach (var side in (TeamSide[])[TeamSide.Blue, TeamSide.Red])
        {
            var chance = TeamRules.PokePushChance(Team(side).Champions, Team(side.Opponent()).Champions);
            if (chance <= 0 || PresentIn(side, lane, t).Count == 0 || _towersDestroyed[(int)side, (int)lane] >= TowersPerLane)
                continue;

            if (TowerRng.NextDouble() < chance)
            {
                TakeTower(side, lane, t, $"{side.DisplayName()} poke'la {LaneName(lane)} koridorda", scored: true);
                return true;
            }

            return false;
        }

        return false;
    }

    // Objektif savaşından sonra itiş. Kaybedenlerin kaçının öldüğüne, kill'ler çözüldükten sonra (t + 12) bakılır.
    private void SchedulePostFightPush(TeamSide winner, Objective objective, IReadOnlyList<ChampionProfile> losers, double t)
    {
        var pushT = t + PostFightPushDelay;
        At(pushT, () =>
        {
            if (losers.Count(c => !_deaths.IsAlive(c, pushT)) < PostFightMinDead)
                return;

            var lane = PushLane(winner, objective);
            if (lane is { } target)
                TakeTower(winner, target, pushT, $"{winner.DisplayName()} savaşın ardından {LaneName(target)} koridorda", scored: false);
        });
    }

    // Ejder → bot, Herald → mid, Baron/Elder → en çok kulesi kalmış koridor (eşitlikte mid, top, bot).
    private Lane? PushLane(TeamSide side, Objective objective)
    {
        Lane[] candidates = objective switch
        {
            Objective.Dragon => [Lane.Bot],
            Objective.Herald => [Lane.Mid],
            _ => [Lane.Mid, Lane.Top, Lane.Bot],
        };

        var open = candidates.Where(l => _towersDestroyed[(int)side, (int)l] < TowersPerLane).ToList();
        return open.Count == 0 ? null : open.MinBy(l => _towersDestroyed[(int)side, (int)l]);
    }

    private static string TowerName(int tier) => tier switch
    {
        0 => "dış kuleyi",
        1 => "iç kuleyi",
        2 => "inhibitörü",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
