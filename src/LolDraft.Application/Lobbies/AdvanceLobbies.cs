using LolDraft.Application.Matches;
using LolDraft.Domain;

namespace LolDraft.Application.Lobbies;

/// <summary>
/// Lobi saati (DESIGN.md §8.13, §8.14): her tikte bütün lobileri sırayla, her birini kendi kilidi altında ilerletir
/// (<see cref="Lobby.Advance"/>). Durumu değişenlerin yeni görüntüsünü ve kapananların kodunu döndürür;
/// kapanan lobi depodan burada, kilidin içinde silinir. Yayını Api yapar.
/// Zamanlayıcı bilmez: kim, ne sıklıkla çağıracağına dışarısı karar verir (test beklemeden çağırır).
/// Swap'ın süresi dolup draft biterse maç da aynı kilidin altında oynanır (<see cref="PlayMatch"/>).
/// </summary>
public sealed class AdvanceLobbies(
    ILobbyRepository lobbies, IClock clock, IDraftRandom random, PlayMatch playMatch, IMatchRepository matches)
{
    public async Task<AdvanceReport> Handle(CancellationToken ct)
    {
        var changed = new List<LobbyView>();
        var closed = new List<LobbyCode>();
        var played = new List<MatchRecord>();

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

                var match = tick == LobbyTick.Changed ? playMatch.PlayIfDrafted(lobby, now) : null;
                return Result<(LobbyTick, LobbyView?, MatchRecord?)>.Ok(
                    (tick, tick == LobbyTick.Changed ? LobbyView.Of(lobby, now) : null, match));
            }, ct);

            // Lobi bu arada silindiyse (NotFound) atlanır.
            if (result.IsFailure)
                continue;

            var (tick, view, match) = result.Value;
            if (tick == LobbyTick.Closed)
                closed.Add(code);
            else if (view is not null)
                changed.Add(view);

            // Kayıt kilidin dışında, yayından önce (LobbyCommands ile aynı sıra). İptal edilemez: kapanışta tik
            // yarıda kesilse bile oynanmış (seriye yazılmış) maç kaydedilir.
            if (match is not null)
            {
                await matches.Save(match, CancellationToken.None);
                played.Add(match);
            }
        }

        return new AdvanceReport(changed, closed, played);
    }
}

/// <summary>Bir tikin sonucu: durumu değişen lobiler (yeni görüntüleriyle), kapanan lobiler ve oynanan maçlar.</summary>
public sealed record AdvanceReport(
    IReadOnlyList<LobbyView> Changed, IReadOnlyList<LobbyCode> Closed, IReadOnlyList<MatchRecord> Played);
