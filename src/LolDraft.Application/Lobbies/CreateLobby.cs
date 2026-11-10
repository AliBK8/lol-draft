using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Lobi açar: kod ve oyuncu kimliği üretir, açanı A koltuğuna oturtur, token verir.</summary>
public sealed class CreateLobby(
    ILobbyRepository lobbies, IChampionCatalog catalog, ISecureRandom random, IGuestTokenService tokens)
{
    public async Task<Result<LobbyTicket>> Handle(SeriesFormat format, bool fearless, CancellationToken ct)
    {
        var code = LobbyCode.FromBytes(random.Bytes(LobbyCode.ByteLength));
        var host = PlayerId.FromBytes(random.Bytes(PlayerId.ByteLength));

        // Yazı tura: ilk maçta mavi kim? Açan A koltuğunda ama rengi şans belirler (çift bayt → A).
        var firstBlue = random.Bytes(1)[0] % 2 == 0 ? TeamSlot.A : TeamSlot.B;

        var lobby = Lobby.Create(code, host, format, fearless, catalog.All, firstBlue);
        if (lobby.IsFailure)
            return Result<LobbyTicket>.Fail(lobby.Error!);

        await lobbies.Add(lobby.Value, ct);

        var identity = new GuestIdentity(host, code, TeamSlot.A);
        return Result<LobbyTicket>.Ok(new LobbyTicket(identity, tokens.Issue(identity)));
    }
}
