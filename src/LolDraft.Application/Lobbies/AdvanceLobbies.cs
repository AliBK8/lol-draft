using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Lobi saati (DESIGN.md §8.13): her tikte bütün lobileri sırayla, her birini kendi kilidi altında ilerletir
/// (<see cref="Lobby.Advance"/>). Durumu değişen lobilerin yeni görüntüsünü döndürür; yayını Api yapar.
/// Zamanlayıcı bilmez: kim, ne sıklıkla çağıracağına dışarısı karar verir (test beklemeden çağırır).
/// </summary>
public sealed class AdvanceLobbies(ILobbyRepository lobbies, IClock clock, IDraftRandom random)
{
    public async Task<IReadOnlyList<LobbyView>> Handle(CancellationToken ct)
    {
        var changed = new List<LobbyView>();
        foreach (var code in lobbies.Codes())
        {
            var result = await lobbies.Execute(code, lobby =>
            {
                // Saat her lobinin kendi kilidi içinde okunur: o lobideki hamlelerle aynı sırada.
                var now = clock.Now;
                return Result<LobbyView?>.Ok(lobby.Advance(now, random) ? LobbyView.Of(lobby, now) : null);
            }, ct);

            // Lobi bu arada silindiyse (NotFound) atlanır.
            if (result is { IsSuccess: true, Value: { } view })
                changed.Add(view);
        }

        return changed;
    }
}
