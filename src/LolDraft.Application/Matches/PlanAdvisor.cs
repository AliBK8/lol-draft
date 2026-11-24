using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application.Matches;

/// <summary>
/// Domain'in dizilim ve planını motorun diline çevirir (DESIGN.md §4.10, §8.3: Domain motoru bilmez) ve planın draft'a
/// uyumunu motordan sorar: arayüzdeki "uyumlu / nötr / uymuyor" rozeti ile maçtaki çarpan aynı kaynaktan gelir.
/// </summary>
public sealed class PlanAdvisor(IChampionCatalog catalog)
{
    private static readonly MatchPlan[] Plans = Enum.GetValues<MatchPlan>();

    /// <summary>Dizilimin her plana uyumu (swap bittikten sonra: uyum yuvaya bakar, ör. bot ikilisinin erken gücü).</summary>
    public IReadOnlyDictionary<MatchPlan, PlanFit> Fits(TeamLineup lineup)
    {
        var team = Team(lineup);
        return Plans.ToDictionary(plan => plan, plan => MatchEngine.PlanFit(team, ToEngine(plan)));
    }

    /// <summary>Planın bu dizilime uyumu.</summary>
    public PlanFit Fit(TeamLineup lineup, MatchPlan plan) => MatchEngine.PlanFit(Team(lineup), ToEngine(plan));

    // Swap sonrası dizilim → motorun takımı: yuva sırası (top, jungle, mid, bot, support) iki tarafta aynı.
    // Şampiyon kendi rolünde değilse ceza ve oynaklığı motor uygular (profildeki doğal roller, DESIGN.md §16.8).
    internal TeamDraft Team(TeamLineup lineup) => new(
        catalog.Profile(lineup[Role.Top]), catalog.Profile(lineup[Role.Jungle]), catalog.Profile(lineup[Role.Mid]),
        catalog.Profile(lineup[Role.Bot]), catalog.Profile(lineup[Role.Support]));

    // Adlar iki tarafta aynı; yeni plan iki yere bilinçli eklenir (eksik eşleme sessizce Dengeli olmasın).
    internal static GamePlan ToEngine(MatchPlan plan) => plan switch
    {
        MatchPlan.Balanced => GamePlan.Balanced,
        MatchPlan.DragonLate => GamePlan.DragonLate,
        MatchPlan.EarlyPressure => GamePlan.EarlyPressure,
        MatchPlan.BotPressure => GamePlan.BotPressure,
        MatchPlan.Split => GamePlan.Split,
        MatchPlan.FastFinish => GamePlan.FastFinish,
        MatchPlan.Invade => GamePlan.Invade,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
    };
}
