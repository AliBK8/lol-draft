using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application.Matches;

/// <summary>
/// Oynanmış bir maç: tekrar izlemenin tek kaynağı dondurulmuş belgedir (<see cref="Replay"/>: olaylar, dakika kareleri,
/// karne). Seed ve motor sürümü sadece teşhis için (aynı seed'le yeniden simülasyon denge ayarından sonra başka maç verir).
/// </summary>
/// <param name="BlueTeam">Bu maçta mavi olan takım koltuğu; motorun renkleri koltuğa bununla çevrilir.</param>
/// <param name="EngineVersion">Motor derlemesinin kimliği (kod değişince değişir; deterministik derleme).</param>
public sealed record MatchRecord(
    MatchId Id, LobbyCode Lobby, int GameNumber, TeamSlot BlueTeam, Seed Seed, string EngineVersion, Instant PlayedAt,
    Replay Replay);

/// <summary>
/// Draft biten maçı oynatır (DESIGN.md §8.15): draft → motor girdisi → simülasyon → <see cref="Lobby.RecordGame"/>.
/// Lobinin kilidi altında çağrılır (hamle ya da saat tikinin içinde): "draft bitti" ile "sonuç yazıldı" arasına
/// başka hamle giremez. Simülasyon ~1 ms, kilidi uzatmaz.
/// </summary>
public sealed class PlayMatch(PlanAdvisor advisor, IMatchSimulator simulator, ISecureRandom random)
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
        // Oyun planları (DESIGN.md §4.10): seçmeyen Dengeli (DraftSession). Uyum replay'e de yazılır (karne).
        var (blue, red) = (advisor.Team(draft.Blue), advisor.Team(draft.Red));
        var (bluePlan, redPlan) = (PlanAdvisor.ToEngine(draft.BluePlan), PlanAdvisor.ToEngine(draft.RedPlan));
        var result = simulator.Simulate(new MatchInput(blue, red, seed, bluePlan, redPlan));
        var plans = new MatchPlans(draft.BluePlan, MatchEngine.PlanFit(blue, bluePlan), draft.RedPlan, MatchEngine.PlanFit(red, redPlan));

        var id = MatchId.FromBytes(random.Bytes(MatchId.ByteLength));
        // Maç numarası ve mavi takım yazmadan önce okunur: RecordGame seriyi bir sonraki maça geçirir.
        // Belge seriye yazmadan önce üretilir: çeviri hata verirse (sözleşmede olmayan olay) skor da yazılmaz.
        var gameNumber = lobby.Series.GameNumber;
        var blueTeam = lobby.Series.BlueTeam;
        var replay = MatchReplay.Create(id, gameNumber, blueTeam, result, plans);
        var record = new MatchRecord(id, lobby.Code, gameNumber, blueTeam, seed, EngineVersion, now, replay);

        var winner = result.End.Winner == TeamSide.Blue ? Side.Blue : Side.Red;
        var recorded = lobby.RecordGame(winner, id, now);
        if (recorded.IsFailure)
            throw new InvalidOperationException($"Biten draft'ın sonucu yazılamadı: {recorded.Error!.Code}");

        return record;
    }
}

/// <summary>Maçın planları ve draft'a uyumları (DESIGN.md §4.10): replay'de, rakibin planı maç sonunda görünür.</summary>
public sealed record MatchPlans(MatchPlan Blue, PlanFit BlueFit, MatchPlan Red, PlanFit RedFit);
