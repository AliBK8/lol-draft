using LolDraft.Application;
using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Infrastructure.Champions;

/// <summary>
/// Şampiyon listesi ve profilleri, derlemeye gömülü data/champions.json'dan (denge aracıyla aynı dosya, aynı okuyucu:
/// <see cref="ChampionFile"/>). Açılışta bir kez okunur; bozuk kayıt varsa uygulama açılmaz.
/// </summary>
internal sealed class EmbeddedChampionCatalog : IChampionCatalog
{
    private const string ResourceName = "champions.json";

    private readonly Dictionary<ChampionId, ChampionProfile> _profiles;

    public EmbeddedChampionCatalog()
    {
        using var stream = typeof(EmbeddedChampionCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} derlemeye gömülmemiş.");

        var entries = ChampionFile.Parse(stream);
        All = [.. entries.Select(c => c.Id)];
        _profiles = entries.ToDictionary(c => c.Id, c => c.Profile);
    }

    public IReadOnlyCollection<ChampionId> All { get; }

    // Draft havuz dışı şampiyonu kabul etmez (not_in_pool); buraya ulaşan kimlik hep havuzdadır.
    public ChampionProfile Profile(ChampionId champion) =>
        _profiles.TryGetValue(champion, out var profile)
            ? profile
            : throw new KeyNotFoundException($"Katalogda olmayan şampiyon: {champion}");
}
