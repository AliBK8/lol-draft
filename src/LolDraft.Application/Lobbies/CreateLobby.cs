using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Lobi açar: kod ve oyuncu kimliği üretir, açanı A koltuğuna oturtur, token verir.</summary>
public sealed class CreateLobby(
    ILobbyRepository lobbies, IChampionCatalog catalog, ISecureRandom random, IGuestTokenService tokens, IClock clock)
{
    /// <summary>Bir istemcinin (IP) aynı anda açık tutabileceği lobi sayısı (DESIGN.md §8.6).</summary>
    public const int MaxOpenLobbiesPerClient = 3;

    public static readonly Error TooManyLobbies =
        new("too_many_lobbies", $"Aynı anda en fazla {MaxOpenLobbiesPerClient} açık lobin olabilir.");

    /// <param name="client">İstemcinin anahtarı (IP): açık lobi sınırı buna göre sayılır.</param>
    public async Task<Result<LobbyTicket>> Handle(SeriesFormat format, bool fearless, string client, CancellationToken ct)
    {
        var code = LobbyCode.FromBytes(random.Bytes(LobbyCode.ByteLength));
        var host = PlayerId.FromBytes(random.Bytes(PlayerId.ByteLength));

        // Yazı tura: ilk maçta mavi kim? Açan A koltuğunda ama rengi şans belirler (çift bayt → A).
        var firstBlue = random.Bytes(1)[0] % 2 == 0 ? TeamSlot.A : TeamSlot.B;

        var lobby = Lobby.Create(code, host, format, fearless, catalog.All, firstBlue, clock.Now);
        if (lobby.IsFailure)
            return Result<LobbyTicket>.Fail(lobby.Error!);

        if (!await lobbies.TryAdd(lobby.Value, client, MaxOpenLobbiesPerClient, ct))
            return Result<LobbyTicket>.Fail(TooManyLobbies);

        var identity = new GuestIdentity(host, code, TeamSlot.A);
        return Result<LobbyTicket>.Ok(new LobbyTicket(identity, tokens.Issue(identity)));
    }
}
