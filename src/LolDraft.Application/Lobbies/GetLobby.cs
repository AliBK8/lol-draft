using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Lobinin durumunu, sadece o lobide oturan oyuncuya verir.</summary>
public sealed class GetLobby(ILobbyRepository lobbies)
{
    public Task<Result<LobbyView>> Handle(GuestIdentity identity, CancellationToken ct) =>
        lobbies.Execute(identity.Lobby, lobby =>
            // Yetki: token geçerli olsa da son söz lobinin. Oyuncu gerçekten o koltukta oturuyor olmalı.
            lobby.SeatOf(identity.Player) == identity.Seat
                ? Result<LobbyView>.Ok(LobbyView.Of(lobby, identity.Seat))
                : Result<LobbyView>.Fail(LobbyErrors.NotInLobby), ct);
}
