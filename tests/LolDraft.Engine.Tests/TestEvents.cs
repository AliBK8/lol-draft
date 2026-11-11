using LolDraft.Engine.Events;

namespace LolDraft.Engine.Tests;

/// <summary>Olay listesinden "kim o an nerede" sorusuna cevap veren test yardımcıları.</summary>
internal static class TestEvents
{
    public static bool IsDead(IReadOnlyList<MatchEvent> events, string id, double t) =>
        events.OfType<KillEvent>().Any(k => k.VictimId == id && k.T <= t && t < k.Until);

    // Meşgul: son 25 sn içinde bir savaşa (çatışma, objektif, kuşatma) katılmış ya da son 20 sn içinde gank'a gitmiş.
    // Objektif ve kuşatmaya o an yaşayan herkes katılır; o an ölü olan katılmaz (dirilince koridoruna döner).
    public static bool IsAway(IReadOnlyList<MatchEvent> events, string id, double t) =>
        events.OfType<MoveEvent>().Any(m =>
            m.ParticipantIds.Contains(id) && m.T <= t && t < m.T + (m.River is null ? 20 : 25))
        || events.Any(e => (e is ObjectiveEvent || (e is SiegeEvent && e.T % 60 == 45)) && e.T <= t && t < e.T + 25
                           && !IsDead(events, id, e.T));

    public static bool IsAbsent(IReadOnlyList<MatchEvent> events, string id, double t) =>
        IsDead(events, id, t) || IsAway(events, id, t);

    // Koridorun "yüzü" (bot'ta adc) ve tüm koridorcular.
    public static IReadOnlyList<ChampionProfile> LaneChampions(TeamDraft team, Lane lane) => lane switch
    {
        Lane.Top => [team.Top],
        Lane.Mid => [team.Mid],
        _ => [team.Bot, team.Support],
    };

    // Objektiften 12 sn sonraki kuleler anlatıcıdan değil, savaş sonrası itişten gelir.
    public static HashSet<double> PostFightTimes(IReadOnlyList<MatchEvent> events) =>
        events.OfType<ObjectiveEvent>().Select(o => o.T + 12).ToHashSet();
}
