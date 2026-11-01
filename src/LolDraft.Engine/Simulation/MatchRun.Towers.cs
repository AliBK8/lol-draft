using LolDraft.Engine.Events;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Kule = fırsat: kuleye ancak koridorda rakip yokken vurulur. Rakip ya ölüdür ya da başka yerde meşguldür
/// (nehir çatışması, objektif savaşı, kuşatma, gank'a giden support).
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

    // Koridor fırsatı: kendi koridorcun koridorda, karşısındaki herkes ölü ya da uzakta.
    private bool HasTowerOpportunity(TeamSide side, Lane lane, double t) =>
        IsInLane(LaneChampions(side, lane)[0], t)
        && LaneChampions(side.Opponent(), lane).All(c => !IsInLane(c, t))
        && _towersDestroyed[(int)side, (int)lane] < TowersPerLane;

    // Kuleyi yıkar: sıradaki kule (dış → iç → inhibitör).
    // scored: koridor fırsatı kendi puanını getirir (koridor kategorisine +4, inhibitör +6). Savaş sonrası kule ise
    // kazanılan savaşın görünür sonucudur; puanı objektif değerinde zaten var, ayrıca eklenmez (kartopunu büyütmesin).
    private void TakeTower(TeamSide side, Lane lane, double t, string text, bool scored)
    {
        var tier = _towersDestroyed[(int)side, (int)lane]++;
        if (scored)
            _scores.Add(LaneCategory(lane), side.Sign() * (tier == 2 ? InhibitorValue : TowerValue));
        _events.Add(new TowerEvent(t, $"{text} {TowerName(tier)} yıktı", side, lane, tier));
    }

    // Anlatıcı anında koridor fırsatı: önce önde olan taraf, sonra diğeri. Kule düştüyse true.
    private bool TryLaneTower(TeamSide ahead, Lane lane, int minute, double t)
    {
        if (minute < TowerMinMinute)
            return false;

        foreach (var side in (TeamSide[])[ahead, ahead.Opponent()])
        {
            if (HasTowerOpportunity(side, lane, t))
            {
                TakeTower(side, lane, t, $"{side.DisplayName()}, {LaneName(lane)} koridorda", scored: true);
                return true;
            }
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
