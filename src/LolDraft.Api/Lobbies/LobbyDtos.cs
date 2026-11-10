using LolDraft.Application.Lobbies;
using LolDraft.Domain;

namespace LolDraft.Api.Lobbies;

// İstemciyle JSON sözleşmesi. Domain tipleri doğrudan serileştirilmez: değer nesneleri {"value": ...} olur,
// enum adı değişince sözleşme sessizce bozulur. Çeviri burada, elle ve tek yerde.

/// <summary>Lobinin durumu: REST GET'te ve hub'ın LobbyUpdated olayında aynı biçim.</summary>
internal sealed record LobbyStateDto(
    string Code,
    string Format,
    bool Fearless,
    bool IsFull,
    int GameNumber,
    string BlueTeam,
    int WinsA,
    int WinsB,
    string? Winner,
    string? EndReason,
    string[] Ready,
    SeatDto[] Seats,
    DraftStateDto? Draft,
    DateTimeOffset ServerNow)
{
    public static LobbyStateDto Of(LobbyView view) => new(
        view.Code.Value,
        Names.Format(view.Format),
        view.Fearless,
        view.IsFull,
        view.GameNumber,
        view.BlueTeam.ToString(),
        view.WinsA,
        view.WinsB,
        view.Winner?.ToString(),
        view.EndReason switch
        {
            SeriesEndReason.Played => "played",
            SeriesEndReason.Forfeit => "forfeit",
            _ => null,
        },
        [.. view.Ready.Select(s => s.ToString())],
        [.. view.Seats.Select(s => new SeatDto(s.Seat.ToString(), s.Connected, s.AbsentSince?.Value))],
        view.Draft is { } draft ? DraftStateDto.Of(draft) : null,
        view.ServerNow.Value);
}

/// <summary>
/// Draft durumu. Geri sayım: kalan = deadline − serverNow; istemci bunu kendi saatiyle çizer
/// (iki saat arasındaki fark serverNow'dan düzeltilir).
/// </summary>
internal sealed record DraftStateDto(
    string Phase,
    string? Turn,
    string? Step,
    DateTimeOffset? Deadline,
    DraftActionDto[] Actions,
    string[]? BlueLineup,
    string[]? RedLineup,
    bool BlueLocked,
    bool RedLocked,
    string[] FearlessLocks)
{
    public static DraftStateDto Of(DraftView view) => new(
        Names.Phase(view.Phase),
        view.Turn is { } turn ? Names.Side(turn) : null,
        view.StepKind is { } kind ? Names.Kind(kind) : null,
        view.Deadline?.Value,
        [.. view.Actions.Select(DraftActionDto.Of)],
        view.BlueLineup?.Champions.Select(c => c.Value).ToArray(),
        view.RedLineup?.Champions.Select(c => c.Value).ToArray(),
        view.BlueLocked,
        view.RedLocked,
        [.. view.FearlessLocks.Select(c => c.Value)]);
}

/// <summary>Koltuk: bağlı mı, değilse ne zamandan beri yok (hükmen sayacı = absentSince + 2 dk).</summary>
internal sealed record SeatDto(string Seat, bool Connected, DateTimeOffset? AbsentSince);

internal sealed record DraftActionDto(int Step, string Side, string Kind, string? Champion, bool ByTimeout)
{
    public static DraftActionDto Of(DraftAction action) =>
        new(action.Step, Names.Side(action.Side), Names.Kind(action.Kind), action.Champion?.Value, action.ByTimeout);
}

/// <summary>Hub hamlesinin cevabı (sadece hamleyi yapana). Başarılıysa yeni durum ayrıca herkese yayınlanır.</summary>
internal sealed record HubResultDto(bool Ok, string? Error, string? Message)
{
    public static readonly HubResultDto Success = new(true, null, null);

    public static HubResultDto Failure(Error error) => new(false, error.Code, error.Message);
}

/// <summary>Domain değerleri ↔ sözleşmedeki adlar.</summary>
internal static class Names
{
    public static string Format(SeriesFormat format) => $"bo{(int)format}";

    public static SeriesFormat? ParseFormat(string? value) => value switch
    {
        "bo1" => SeriesFormat.Bo1,
        "bo3" => SeriesFormat.Bo3,
        "bo5" => SeriesFormat.Bo5,
        _ => null,
    };

    public static string Side(Domain.Side side) => side == Domain.Side.Blue ? "blue" : "red";

    public static string Kind(DraftActionKind kind) => kind == DraftActionKind.Ban ? "ban" : "pick";

    public static string Phase(DraftPhase phase) => phase switch
    {
        DraftPhase.Ban1 => "ban1",
        DraftPhase.Pick1 => "pick1",
        DraftPhase.Ban2 => "ban2",
        DraftPhase.Pick2 => "pick2",
        DraftPhase.Swap => "swap",
        DraftPhase.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    public static Role? ParseRole(string? value) => value switch
    {
        "top" => Role.Top,
        "jungle" => Role.Jungle,
        "mid" => Role.Mid,
        "bot" => Role.Bot,
        "support" => Role.Support,
        _ => null,
    };
}
