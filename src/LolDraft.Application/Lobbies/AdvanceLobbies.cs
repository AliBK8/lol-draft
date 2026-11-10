using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Lobi saati (DESIGN.md §8.13, §8.14): her tikte bütün lobileri sırayla, her birini kendi kilidi altında ilerletir
/// (<see cref="Lobby.Advance"/>). Durumu değişenlerin yeni görüntüsünü ve kapananların kodunu döndürür;
/// kapanan lobi depodan burada, kilidin içinde silinir. Yayını Api yapar.
/// Zamanlayıcı bilmez: kim, ne sıklıkla çağıracağına dışarısı karar verir (test beklemeden çağırır).
/// </summary>
public sealed class AdvanceLobbies(ILobbyRepository lobbies, IClock clock, IDraftRandom random)
{
    public async Task<AdvanceReport> Handle(CancellationToken ct)
    {
        var changed = new List<LobbyView>();
        var closed = new List<LobbyCode>();

        foreach (var code in lobbies.Codes())
        {
            var result = await lobbies.Execute(code, lobby =>
            {
                // Saat her lobinin kendi kilidi içinde okunur: o lobideki hamlelerle aynı sırada.
                var now = clock.Now;
                var tick = lobby.Advance(now, random);

                // Kilidin içinde silinir: kilidi bekleyen hamle sonra "lobi yok" alır, kapanmış lobide oynanmaz.
                if (tick == LobbyTick.Closed)
                    lobbies.Remove(code);

                return Result<(LobbyTick, LobbyView?)>.Ok((tick, tick == LobbyTick.Changed ? LobbyView.Of(lobby, now) : null));
            }, ct);

            // Lobi bu arada silindiyse (NotFound) atlanır.
            if (result.IsFailure)
                continue;

            var (tick, view) = result.Value;
            if (tick == LobbyTick.Closed)
                closed.Add(code);
            else if (view is not null)
                changed.Add(view);
        }

        return new AdvanceReport(changed, closed);
    }
}

/// <summary>Bir tikin sonucu: durumu değişen lobiler (yeni görüntüleriyle) ve kapanan lobiler.</summary>
public sealed record AdvanceReport(IReadOnlyList<LobbyView> Changed, IReadOnlyList<LobbyCode> Closed);
