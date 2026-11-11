using LolDraft.Domain;

namespace LolDraft.Application.Matches;

/// <summary>
/// Oynanmış maçı getirir (tekrar izleme, paylaşılan link). Kimlik bilen herkes izleyebilir: kimlik 128 bit rastgele,
/// link paylaşılmadıkça bulunamaz (lobi kodu gibi). Gateway'de IP başına dakikada 60 (replay politikası).
/// </summary>
public sealed class GetMatch(IMatchRepository matches)
{
    public async Task<Result<MatchRecord>> Handle(MatchId id, CancellationToken ct) =>
        await matches.Get(id, ct) is { } match
            ? Result<MatchRecord>.Ok(match)
            : Result<MatchRecord>.Fail(MatchErrors.NotFound);
}
