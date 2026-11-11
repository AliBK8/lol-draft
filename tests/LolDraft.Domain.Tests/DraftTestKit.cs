using LolDraft.Domain;

namespace LolDraft.Domain.Tests;

/// <summary>Testlerin ortak malzemesi: 40'lık havuz, sabit saat, sabit zar.</summary>
internal static class DraftTestKit
{
    public static readonly Instant T0 = new(new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.Zero));

    /// <summary>"c01".."c40".</summary>
    public static readonly IReadOnlyList<ChampionId> Pool = Enumerable.Range(1, 40).Select(i => Id($"c{i:00}")).ToList();

    public static ChampionId Id(string value) => ChampionId.Create(value).Value;

    /// <summary>Rastgele maç kimliği (RecordGame için; testler simülasyon yapmaz).</summary>
    public static MatchId NewMatch() => MatchId.FromBytes(Guid.NewGuid().ToByteArray());

    public static DraftSession Start(FearlessLocks? locks = null, IEnumerable<ChampionId>? pool = null) =>
        DraftSession.Start(pool ?? Pool, locks ?? FearlessLocks.None, DraftTimings.Default, T0).Value;

    /// <summary>Adım adım oynar: her adımda sıradaki taraf havuzdaki ilk uygun şampiyonu ban/pick eder.</summary>
    public static void PlaySteps(DraftSession session, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var step = session.CurrentStep!;
            var champion = Pool.First(c => session.Actions.All(a => a.Champion != c));
            var result = step.Kind == DraftActionKind.Ban
                ? session.Ban(step.Side, champion, T0)
                : session.Pick(step.Side, champion, T0);
            Assert.True(result.IsSuccess, result.Error?.Code);
        }
    }

    public static ChampionId[] Picks(DraftSession session, Side side) =>
        session.Actions.Where(a => a.Side == side && a.Kind == DraftActionKind.Pick).Select(a => a.Champion!.Value).ToArray();

    public sealed class FixedRandom(int value) : IDraftRandom
    {
        public int Next(int maxExclusive) => Math.Min(value, maxExclusive - 1);
    }
}
