using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>Davet koduyla lobiye katılır: boş koltuğa oturur, token alır.</summary>
public sealed class JoinLobby(ILobbyRepository lobbies, ISecureRandom random, IGuestTokenService tokens, IClock clock)
{
    public async Task<Result<LobbyTicket>> Handle(LobbyCode code, CancellationToken ct)
    {
        var player = PlayerId.FromBytes(random.Bytes(PlayerId.ByteLength));
        var (rejoinToken, rejoinKey) = RejoinSecrets.New(random);

        // Oturma lobinin kilidi altında: aynı anda gelen iki katılmadan sadece biri koltuğu alır.
        // Koltuk anahtarı aynı kilit içinde, oturur oturmaz: anahtarsız koltuk dışarıdan görülmez.
        var seat = await lobbies.Execute(code, lobby =>
        {
            var joined = lobby.Join(player, clock.Now);
            if (joined.IsSuccess)
                lobby.AssignRejoinKey(player, rejoinKey);
            return joined;
        }, ct);
        if (seat.IsFailure)
            return Result<LobbyTicket>.Fail(seat.Error!);

        // Token ancak koltuk alındıktan sonra üretilir: koltuksuz oyuncunun token'ı olmaz.
        var identity = new GuestIdentity(player, code, seat.Value);
        return Result<LobbyTicket>.Ok(new LobbyTicket(identity, tokens.Issue(identity), rejoinToken));
    }
}
