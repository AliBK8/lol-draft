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
                           && !IsDead(events, id, e.T))
        // Yakalanma (DESIGN.md §4.11 D): tek başına yakalamada avcılar 20 sn; yardım gelip savaş olduysa herkes 25 sn.
        || events.OfType<CatchEvent>().Any(c => c.T <= t
            && (c.HelperIds.Count == 0
                ? c.HunterIds.Contains(id) && t < c.T + 20
                : (c.HunterIds.Contains(id) || c.HelperIds.Contains(id) || c.VictimId == id) && t < c.T + 25));

    public static bool IsAbsent(IReadOnlyList<MatchEvent> events, string id, double t) =>
        IsDead(events, id, t) || IsAway(events, id, t);

    // Koridorun "yüzü" (bot'ta adc) ve tüm koridorcular.
    public static IReadOnlyList<ChampionProfile> LaneChampions(TeamDraft team, Lane lane) => lane switch
    {
        Lane.Top => [team.Top],
        Lane.Mid => [team.Mid],
        _ => [team.Bot, team.Support],
    };

    // Harita düzeni (DESIGN.md §4.11): takımın t anındaki son LayoutEvent'i; yoksa standart yerleşim.
    private static readonly Lane?[] StandardPosts = [Lane.Top, null, Lane.Mid, Lane.Bot, Lane.Bot];

    public static IReadOnlyList<Lane?> PostsAt(IReadOnlyList<MatchEvent> events, TeamSide side, double t) =>
        events.OfType<LayoutEvent>().LastOrDefault(l => l.Side == side && l.T <= t)?.Posts ?? StandardPosts;

    // Düzene göre t anında koridorda duran şampiyonlar (ölü ya da meşgul olsa da).
    public static IReadOnlyList<ChampionProfile> PostedIn(IReadOnlyList<MatchEvent> events, TeamDraft team, TeamSide side, Lane lane, double t)
    {
        var posts = PostsAt(events, side, t);
        return [.. team.Champions.Where((_, i) => posts[i] == lane)];
    }

    // Objektiften 12 sn sonraki kuleler anlatıcıdan değil, savaş sonrası itişten gelir.
    public static HashSet<double> PostFightTimes(IReadOnlyList<MatchEvent> events) =>
        events.OfType<ObjectiveEvent>().Select(o => o.T + 12).ToHashSet();
}
