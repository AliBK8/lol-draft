using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application.Matches;

/// <summary>
/// Oynanmış bir maç: tekrar izlemenin tek kaynağı (olay listesi, dakika kareleri, karne).
/// Seed ve motor sürümü sadece teşhis için (aynı seed'le yeniden simülasyon denge ayarından sonra başka maç verir).
/// </summary>
/// <param name="BlueTeam">Bu maçta mavi olan takım koltuğu; motorun renkleri koltuğa bununla çevrilir.</param>
/// <param name="EngineVersion">Motor derlemesinin kimliği (kod değişince değişir; deterministik derleme).</param>
public sealed record MatchRecord(
    MatchId Id, LobbyCode Lobby, int GameNumber, TeamSlot BlueTeam, Seed Seed, string EngineVersion, Instant PlayedAt,
    MatchResult Result);

/// <summary>
/// Draft biten maçı oynatır (DESIGN.md §8.15): draft → motor girdisi → simülasyon → <see cref="Lobby.RecordGame"/>.
/// Lobinin kilidi altında çağrılır (hamle ya da saat tikinin içinde): "draft bitti" ile "sonuç yazıldı" arasına
/// başka hamle giremez. Simülasyon ~1 ms, kilidi uzatmaz.
/// </summary>
public sealed class PlayMatch(IChampionCatalog catalog, IMatchSimulator simulator, ISecureRandom random)
{
    private static readonly string EngineVersion =
        typeof(IMatchSimulator).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>
    /// Lobinin draft'ı bittiyse maçı oynatır, sonucu seriye yazar ve kaydı döndürür; bitmediyse (ya da draft yoksa) null.
    /// Kayıt kilidin dışında depoya yazılır (depo async).
    /// </summary>
    public MatchRecord? PlayIfDrafted(Lobby lobby, Instant now)
    {
        ArgumentNullException.ThrowIfNull(lobby);

        if (lobby.Series.CurrentDraft?.Outcome is not { } draft)
            return null;

        // Seed CSPRNG'den: oyuncular tahmin edemez (DESIGN.md §8.6) ve istemciye hiç gönderilmez.
        var seed = new Seed(BitConverter.ToUInt32(random.Bytes(sizeof(uint))));
        var result = simulator.Simulate(new MatchInput(Team(draft.Blue), Team(draft.Red), seed));

        var id = MatchId.FromBytes(random.Bytes(MatchId.ByteLength));
        // Maç numarası ve mavi takım yazmadan önce okunur: RecordGame seriyi bir sonraki maça geçirir.
        var record = new MatchRecord(
            id, lobby.Code, lobby.Series.GameNumber, lobby.Series.BlueTeam, seed, EngineVersion, now, result);

        var winner = result.End.Winner == TeamSide.Blue ? Side.Blue : Side.Red;
        var recorded = lobby.RecordGame(winner, id, now);
        if (recorded.IsFailure)
            throw new InvalidOperationException($"Biten draft'ın sonucu yazılamadı: {recorded.Error!.Code}");

        return record;
    }

    // Swap sonrası dizilim → motorun takımı: yuva sırası (top, jungle, mid, bot, support) iki tarafta aynı.
    // Şampiyon kendi rolünde değilse ceza ve oynaklığı motor uygular (profildeki doğal roller, DESIGN.md §16.8).
    private TeamDraft Team(TeamLineup lineup) => new(
        catalog.Profile(lineup[Role.Top]), catalog.Profile(lineup[Role.Jungle]), catalog.Profile(lineup[Role.Mid]),
        catalog.Profile(lineup[Role.Bot]), catalog.Profile(lineup[Role.Support]));
}
