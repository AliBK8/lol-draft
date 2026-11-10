using System.Text.Json;
using LolDraft.Application;
using LolDraft.Domain;

namespace LolDraft.Infrastructure.Champions;

/// <summary>
/// Şampiyon listesi, derlemeye gömülü champions.json'dan (tools/LolDraft.Balance ile aynı dosya).
/// Açılışta bir kez okunur; bozuk kimlik varsa uygulama açılmaz.
/// </summary>
internal sealed class EmbeddedChampionCatalog : IChampionCatalog
{
    private const string ResourceName = "champions.json";

    public EmbeddedChampionCatalog()
    {
        using var stream = typeof(EmbeddedChampionCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} derlemeye gömülmemiş.");
        using var document = JsonDocument.Parse(stream);

        var ids = new List<ChampionId>();
        foreach (var champion in document.RootElement.GetProperty("champions").EnumerateArray())
        {
            var raw = champion.GetProperty("id").GetString();
            var id = ChampionId.Create(raw);
            if (id.IsFailure)
                throw new InvalidDataException($"{ResourceName}: geçersiz şampiyon kimliği '{raw}'.");
            ids.Add(id.Value);
        }

        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidDataException($"{ResourceName}: aynı şampiyon iki kez yazılmış.");

        All = ids;
    }

    public IReadOnlyCollection<ChampionId> All { get; }
}
