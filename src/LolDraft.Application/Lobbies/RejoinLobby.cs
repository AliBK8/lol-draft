using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Koltuk anahtarıyla koltuğu geri alır (DESIGN.md §8.4): sekmeyi kapatıp linki yeniden açan oyuncu.
/// Adımlar: anahtarın özeti → lobinin kilidi altında eşleşen koltuk, anahtar yenisiyle değişir →
/// aynı oyuncu ve koltuk için yeni token + yeni anahtar. Eski token süresi dolana kadar geçerli kalır (karar 2026-11-17).
/// </summary>
public sealed class RejoinLobby(ILobbyRepository lobbies, ISecureRandom random, IGuestTokenService tokens, IClock clock)
{
    public async Task<Result<LobbyTicket>> Handle(LobbyCode code, string? rejoinToken, CancellationToken ct)
    {
        // Biçimi bozuk anahtar özetlenmez, lobiye de gidilmez.
        if (RejoinSecrets.KeyOf(rejoinToken) is not { } presented)
            return Result<LobbyTicket>.Fail(LobbyErrors.RejoinRejected);

        var (nextToken, nextKey) = RejoinSecrets.New(random);
        var back = await lobbies.Execute(code, lobby => lobby.Rejoin(presented, nextKey, clock.Now), ct);
        if (back.IsFailure)
            return Result<LobbyTicket>.Fail(back.Error!);

        var identity = new GuestIdentity(back.Value.Player, code, back.Value.Seat);
        return Result<LobbyTicket>.Ok(new LobbyTicket(identity, tokens.Issue(identity), nextToken));
    }
}
