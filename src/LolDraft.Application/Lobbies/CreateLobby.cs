using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Sunucunun aynı anda tutacağı açık lobi sayısı (DESIGN.md §8.6). Kişi başı sınır tek kaynaktan gelen seli durdurur;
/// bu tavan çok kaynaktan geleni (botnet, çok adres): bellek dolmaz, açık oyunlar etkilenmez, yenisi 503 alır.
/// Lobi küçük (birkaç KB); 2.000 lobi = en fazla 4.000 oyuncu, tek kopyalı sunucu için bol.
/// </summary>
public sealed record LobbyCapacity(int MaxOpenLobbies)
{
    public static LobbyCapacity Default { get; } = new(2000);
}

/// <summary>Lobi açar: kod ve oyuncu kimliği üretir, açanı A koltuğuna oturtur, token verir.</summary>
public sealed class CreateLobby(
    ILobbyRepository lobbies, IChampionCatalog catalog, ISecureRandom random, IGuestTokenService tokens, IClock clock,
    LobbyCapacity capacity)
{
    /// <summary>Bir istemcinin (IPv4 adresi ya da IPv6 /64) aynı anda açık tutabileceği lobi sayısı (DESIGN.md §8.6).</summary>
    public const int MaxOpenLobbiesPerClient = 3;

    public static readonly Error TooManyLobbies =
        new("too_many_lobbies", $"Aynı anda en fazla {MaxOpenLobbiesPerClient} açık lobin olabilir.");

    public static readonly Error ServerBusy =
        new("server_busy", "Sunucu şu an dolu, biraz sonra tekrar dene.");

    /// <param name="client">İstemcinin anahtarı (gateway'in yazdığı; IPv6'da /64): açık lobi sınırı buna göre sayılır.</param>
    public async Task<Result<LobbyTicket>> Handle(SeriesFormat format, bool fearless, string client, CancellationToken ct)
    {
        var code = LobbyCode.FromBytes(random.Bytes(LobbyCode.ByteLength));
        var host = PlayerId.FromBytes(random.Bytes(PlayerId.ByteLength));

        // Yazı tura: ilk maçta mavi kim? Açan A koltuğunda ama rengi şans belirler (çift bayt → A).
        var firstBlue = random.Bytes(1)[0] % 2 == 0 ? TeamSlot.A : TeamSlot.B;

        var lobby = Lobby.Create(code, host, format, fearless, catalog.All, firstBlue, clock.Now);
        if (lobby.IsFailure)
            return Result<LobbyTicket>.Fail(lobby.Error!);

        var added = await lobbies.TryAdd(lobby.Value, client, MaxOpenLobbiesPerClient, capacity.MaxOpenLobbies, ct);
        if (added != LobbyAddResult.Added)
            return Result<LobbyTicket>.Fail(added == LobbyAddResult.ServerFull ? ServerBusy : TooManyLobbies);

        var identity = new GuestIdentity(host, code, TeamSlot.A);
        return Result<LobbyTicket>.Ok(new LobbyTicket(identity, tokens.Issue(identity)));
    }
}
