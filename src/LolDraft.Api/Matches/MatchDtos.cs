using LolDraft.Application.Matches;
using LolDraft.Engine;
using LolDraft.Engine.Events;

namespace LolDraft.Api.Matches;

// Maçın JSON sözleşmesi (MatchReady olayı ve GET /api/matches/{id} aynı biçim). Motor tipleri doğrudan serileştirilmez
// (Engine JSON bilmez, DESIGN.md §8.3); çeviri burada, elle. Olay tipleri DESIGN.md §4.9 tablosundaki adlarla.
// Dışarı çıkmayanlar: seed (DESIGN.md §8.6) ve puan dökümü (ScoreSheet, v2'den beri karara girmiyor).

/// <param name="BlueTeam">Bu maçta mavi olan koltuk (A/B): istemci renkleri koltuğa bununla çevirir.</param>
internal sealed record MatchDto(
    string Id, int GameNumber, string BlueTeam, MatchEndDto End, object[] Events, GoldFrameDto[] GoldFrames,
    ReportDto Report)
{
    public static MatchDto Of(MatchRecord match)
    {
        var result = match.Result;
        return new MatchDto(
            match.Id.Value,
            match.GameNumber,
            match.BlueTeam.ToString(),
            new MatchEndDto(result.End.Minute, EngineNames.Side(result.End.Winner), EngineNames.EndKind(result.End.Kind),
                result.End.WinnerProbability),
            [.. result.Events.Select(Event)],
            [.. result.GoldFrames.Select(f => new GoldFrameDto(f.Minute,
                [.. f.Champions.Select(c => new ChampionGoldDto(c.ChampionId, EngineNames.Side(c.Side), c.Gold, c.Cs))]))],
            ReportDto.Of(result.Report));
    }

    // Her olay: type + t (oyun saniyesi) + text (boşsa log'a yazılmaz, sadece haritayı yönlendirir) + kendi alanları.
    private static object Event(MatchEvent e) => e switch
    {
        KillEvent k => new
        {
            type = "kill", t = k.T, text = k.Text, side = EngineNames.Side(k.Side), killer = k.KillerId,
            victim = k.VictimId, until = k.Until, lane = EngineNames.Lane(k.Lane), bounty = k.Bounty,
        },
        TowerEvent w => new
        {
            type = "tower", t = w.T, text = w.Text, side = EngineNames.Side(w.Side), lane = EngineNames.Lane(w.Lane),
            tier = w.Tier,
        },
        PlateEvent p => new { type = "plate", t = p.T, text = p.Text, side = EngineNames.Side(p.Side), lane = EngineNames.Lane(p.Lane) },
        CsEvent c => new { type = "cs", t = c.T, text = c.Text, side = EngineNames.Side(c.Side), lane = EngineNames.Lane(c.Lane) },
        SurpriseEvent s => new { type = "surprise", t = s.T, text = s.Text, side = EngineNames.Side(s.Side), lane = EngineNames.Lane(s.Lane) },
        GankFailEvent g => new { type = "gankfail", t = g.T, text = g.Text, side = EngineNames.Side(g.Side), lane = EngineNames.Lane(g.Lane) },
        MoveEvent m => new
        {
            type = "move", t = m.T, text = m.Text, participants = m.ParticipantIds, lane = EngineNames.Lane(m.Lane),
            river = EngineNames.River(m.River), lead = m.Lead, tail = m.Tail,
        },
        SkirmishEvent s => new
        {
            type = "skirmish", t = s.T, text = s.Text, side = EngineNames.Side(s.Side), probability = s.Probability,
            river = EngineNames.River(s.River), blueCount = s.BlueCount, redCount = s.RedCount,
        },
        ObjectiveEvent o => new
        {
            type = "objective", t = o.T, text = o.Text, objective = EngineNames.Objective(o.Objective),
            side = EngineNames.Side(o.Side), probability = o.Probability,
        },
        SiegeEvent s => new { type = "siege", t = s.T, text = s.Text, side = EngineNames.Side(s.Side), lead = s.Lead, tail = s.Tail },
        SplitEvent s => new
        {
            type = "split", t = s.T, text = s.Text, side = EngineNames.Side(s.Side), lane = EngineNames.Lane(s.Lane),
            splitter = s.SplitterId, answerer = s.AnswererId,
        },
        DragonSoulEvent d => new { type = "dragonSoul", t = d.T, text = d.Text, side = EngineNames.Side(d.Side) },
        ThrowEvent w => new { type = "throw", t = w.T, text = w.Text, side = EngineNames.Side(w.Side), probability = w.Probability },
        NexusEvent n => new { type = "nexus", t = n.T, text = n.Text, side = EngineNames.Side(n.Side), probability = n.Probability },
        // Yeni olay tipi sessizce kaybolmasın: sözleşmeye bilinçli eklenir.
        _ => throw new InvalidOperationException($"Sözleşmede olmayan olay tipi: {e.GetType().Name}"),
    };
}

internal sealed record MatchEndDto(int Minute, string Winner, string Kind, double WinnerProbability);

internal sealed record GoldFrameDto(int Minute, ChampionGoldDto[] Champions);

internal sealed record ChampionGoldDto(string Champion, string Side, int Gold, int Cs);

/// <summary>Draft karnesi (DESIGN.md §16.11): etkili kurallar, hiper taşıyıcılar, altın kaynakları, rol dışı kumarlar.</summary>
internal sealed record ReportDto(RuleDto[] Rules, CarryDto[] Carries, TeamGoldDto Blue, TeamGoldDto Red, OffRoleDto[] OffRoles)
{
    public static ReportDto Of(DraftReport report) => new(
        [.. report.Rules.Select(r => new RuleDto(r.Rule, EngineNames.Side(r.Side), EngineNames.Context(r.Context),
            r.ChampionId, r.Count, r.Impact, r.Text))],
        [.. report.Carries.Select(c => new CarryDto(c.ChampionId, EngineNames.Side(c.Side), c.Minute, c.PotentialShare, c.Text))],
        TeamGoldDto.Of(report.Blue),
        TeamGoldDto.Of(report.Red),
        [.. report.OffRoles.Select(o => new OffRoleDto(o.ChampionId, EngineNames.Side(o.Side), EngineNames.Role(o.Role),
            o.Penalty, o.GoldDiff, o.Text))]);
}

internal sealed record RuleDto(string Rule, string Side, string Context, string Champion, int Count, double Impact, string Text);

internal sealed record CarryDto(string Champion, string Side, int Minute, double PotentialShare, string Text);

internal sealed record OffRoleDto(string Champion, string Side, string Role, double Penalty, int GoldDiff, string Text);

internal sealed record TeamGoldDto(
    int Passive, int Farm, int SupportItem, int Kill, int Bounty, int Assist, int Plate, int Tower, int Objective, int Total)
{
    public static TeamGoldDto Of(TeamGoldReport g) =>
        new(g.Passive, g.Farm, g.SupportItem, g.Kill, g.Bounty, g.Assist, g.Plate, g.Tower, g.Objective, g.Total);
}

/// <summary>Motor değerleri ↔ sözleşmedeki adlar (Domain'inkiler LobbyDtos.Names'te).</summary>
internal static class EngineNames
{
    public static string Side(TeamSide side) => side == TeamSide.Blue ? "blue" : "red";

    public static string? Lane(Lane? lane) => lane switch
    {
        Engine.Lane.Top => "top",
        Engine.Lane.Mid => "mid",
        Engine.Lane.Bot => "bot",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
    };

    public static string? River(RiverSide? river) => river switch
    {
        RiverSide.Top => "top",
        RiverSide.Bottom => "bottom",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(river), river, null),
    };

    public static string Objective(Objective objective) => objective switch
    {
        Engine.Events.Objective.Dragon => "dragon",
        Engine.Events.Objective.Herald => "herald",
        Engine.Events.Objective.Baron => "baron",
        Engine.Events.Objective.Elder => "elder",
        Engine.Events.Objective.FinalFight => "finalFight",
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective, null),
    };

    public static string EndKind(MatchEndKind kind) => kind == MatchEndKind.Siege ? "siege" : "finalFight";

    // Yuva adları Domain'deki swap sözleşmesiyle aynı (LobbyDtos.Names.ParseRole).
    public static string Role(ChampionRole role) => role switch
    {
        ChampionRole.Top => "top",
        ChampionRole.Jungle => "jungle",
        ChampionRole.Mid => "mid",
        ChampionRole.Bot => "bot",
        ChampionRole.Support => "support",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static string Context(RuleContext context) => context switch
    {
        RuleContext.TopLane => "topLane",
        RuleContext.MidLane => "midLane",
        RuleContext.BotLane => "botLane",
        RuleContext.Gank => "gank",
        RuleContext.Skirmish => "skirmish",
        RuleContext.Objective => "objective",
        RuleContext.Siege => "siege",
        RuleContext.FinalFight => "finalFight",
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
    };
}
