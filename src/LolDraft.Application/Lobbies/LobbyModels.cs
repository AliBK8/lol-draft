using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Token'ın taşıdığı kimlik: kim, hangi lobide, hangi koltukta.</summary>
public sealed record GuestIdentity(PlayerId Player, LobbyCode Lobby, TeamSlot Seat);

/// <summary>Lobiye giren oyuncuya verilen bilet: kimliği ve onu kanıtlayan token.</summary>
public sealed record LobbyTicket(GuestIdentity Identity, string Token);

/// <summary>Lobinin o anki görüntüsü (kopya; canlı lobi dışarı çıkmaz).</summary>
public sealed record LobbyView(
    LobbyCode Code,
    SeriesFormat Format,
    bool Fearless,
    TeamSlot YourSeat,
    bool IsFull,
    int GameNumber,
    TeamSlot BlueTeam,
    int WinsA,
    int WinsB)
{
    public static LobbyView Of(Lobby lobby, TeamSlot yourSeat) => new(
        lobby.Code,
        lobby.Series.Format,
        lobby.Series.Fearless,
        yourSeat,
        lobby.IsFull,
        lobby.Series.GameNumber,
        lobby.Series.BlueTeam,
        lobby.Series.Wins(TeamSlot.A),
        lobby.Series.Wins(TeamSlot.B));
}
