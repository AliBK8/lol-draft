using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Lobinin durumunu, sadece o lobide oturan oyuncuya verir.</summary>
public sealed class GetLobby(ILobbyRepository lobbies, IClock clock)
{
    public Task<Result<SeatedLobbyView>> Handle(GuestIdentity identity, CancellationToken ct) =>
        lobbies.Execute(identity.Lobby, lobby =>
            // Yetki: token geçerli olsa da son söz lobinin. Oyuncu gerçekten o koltukta oturuyor olmalı.
            lobby.SeatOf(identity.Player) == identity.Seat
                ? Result<SeatedLobbyView>.Ok(new SeatedLobbyView(identity.Seat, LobbyView.Of(lobby, clock.Now)))
                : Result<SeatedLobbyView>.Fail(LobbyErrors.NotInLobby), ct);
}
