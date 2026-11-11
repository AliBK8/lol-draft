using LolDraft.Application;
using LolDraft.Domain;

namespace LolDraft.Infrastructure.Champions;

/// <summary>
/// Şampiyon listesi, derlemeye gömülü data/champions.json'dan (denge aracıyla aynı dosya, aynı okuyucu:
/// <see cref="ChampionFile"/>). Açılışta bir kez okunur; bozuk kayıt varsa uygulama açılmaz.
/// </summary>
internal sealed class EmbeddedChampionCatalog : IChampionCatalog
{
    private const string ResourceName = "champions.json";

    public EmbeddedChampionCatalog()
    {
        using var stream = typeof(EmbeddedChampionCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} derlemeye gömülmemiş.");

        All = [.. ChampionFile.Parse(stream).Select(c => c.Id)];
    }

    public IReadOnlyCollection<ChampionId> All { get; }
}
