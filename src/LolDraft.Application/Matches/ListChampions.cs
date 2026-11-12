using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Application.Matches;

/// <summary>
/// İstemcinin bilmesi gereken şampiyon bilgisi (DESIGN.md §8.17): kimlik, ad, doğal roller ve yuva başına rol dışı
/// cezası (swap ekranı, §16.8). Gizli motor sayıları (e/m/l, etiketler, sınıf) dışarı çıkmaz (§0 "matematik gizli").
/// </summary>
/// <param name="OffRole">Yuva adı → ceza (0 = kendi rolü). Yuva adları swap sözleşmesiyle aynı: top, jungle, mid, bot, support.</param>
public sealed record ChampionInfo(string Id, string Name, IReadOnlyList<string> Roles, IReadOnlyDictionary<string, double> OffRole);

/// <summary>Şampiyon listesi: veri derlemeye gömülü ve değişmez; bir kez hesaplanır.</summary>
public sealed class ListChampions
{
    private static readonly ChampionRole[] Slots =
        [ChampionRole.Top, ChampionRole.Jungle, ChampionRole.Mid, ChampionRole.Bot, ChampionRole.Support];

    private readonly IReadOnlyList<ChampionInfo> _champions;

    public ListChampions(IChampionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _champions = [.. catalog.All.Select(id => Info(catalog.Profile(id)))];
    }

    public IReadOnlyList<ChampionInfo> Handle() => _champions;

    private static ChampionInfo Info(ChampionProfile champion) => new(
        champion.Id,
        champion.Name,
        [.. champion.Roles.Select(EngineNames.Role)],
        Slots.ToDictionary(EngineNames.Role, slot => MatchEngine.OffRolePenalty(champion, slot)));
}
