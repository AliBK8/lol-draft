using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application.Matches;

/// <summary>Oyun planı ve uyumun sözleşmedeki adları (lobi durumu, hub, replay aynı adları kullanır).</summary>
public static class PlanNames
{
    public static string Plan(MatchPlan plan) => plan switch
    {
        MatchPlan.Balanced => "balanced",
        MatchPlan.DragonLate => "dragonLate",
        MatchPlan.EarlyPressure => "earlyPressure",
        MatchPlan.BotPressure => "botPressure",
        MatchPlan.Split => "split",
        MatchPlan.FastFinish => "fastFinish",
        MatchPlan.Invade => "invade",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
    };

    public static MatchPlan? ParsePlan(string? value) =>
        Enum.GetValues<MatchPlan>().Select(p => (MatchPlan?)p).FirstOrDefault(p => Plan(p!.Value) == value);

    public static string Fit(PlanFit fit) => fit switch
    {
        PlanFit.Good => "good",
        PlanFit.Neutral => "neutral",
        PlanFit.Poor => "poor",
        _ => throw new ArgumentOutOfRangeException(nameof(fit), fit, null),
    };
}
