using System.Text.Json;
using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Infrastructure.Champions;

/// <summary>Dosyadaki bir şampiyon: kimliği, motor profili ve oynayabildiği roller (dosya sırasıyla).</summary>
public sealed record ChampionEntry(ChampionId Id, ChampionProfile Profile, IReadOnlyList<Role> Roles);

/// <summary>
/// Şampiyon dosyasının (data/champions.json, DESIGN.md §16.2 biçimi) tek okuyucusu. Oyun kataloğu da denge aracı da
/// bunu kullanır: iki ayrı ayrıştırıcı zamanla ayrışır, araçla ölçülen oyun gerçek oyundan farklı olurdu.
/// Bozuk kayıt (bilinmeyen sınıf/rol, aralık dışı değer, geçersiz ya da tekrar eden kimlik) InvalidDataException fırlatır:
/// sunucu bozuk havuzla açılmaz.
/// </summary>
public static class ChampionFile
{
    public static IReadOnlyList<ChampionEntry> Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var data = JsonSerializer.Deserialize<PoolFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException("Şampiyon dosyası boş.");

        var entries = new List<ChampionEntry>();
        var seen = new HashSet<ChampionId>();
        foreach (var c in data.Champions ?? [])
        {
            var id = ChampionId.Create(c.Id);
            if (id.IsFailure)
                throw new InvalidDataException($"Geçersiz şampiyon kimliği '{c.Id}'.");
            if (!seen.Add(id.Value))
                throw new InvalidDataException($"{c.Id}: aynı şampiyon iki kez yazılmış.");
            if (c.Classes is not { Count: 1 or 2 })
                throw new InvalidDataException($"{c.Id}: 1 ya da 2 sınıf olmalı.");
            if (c.Roles is not { Count: > 0 })
                throw new InvalidDataException($"{c.Id}: en az bir rol olmalı.");

            var t = c.Tags ?? new TagsEntry();
            ChampionProfile profile;
            try
            {
                profile = new ChampionProfile(
                    c.Id!, c.Name!, ParseRange(c.Range), ParseClass(c.Classes[0]),
                    c.Classes.Count == 2 ? ParseClass(c.Classes[1]) : null,
                    c.E, c.M, c.L, c.Econ, c.Ap,
                    new ChampionTags(t.Fl, t.En, t.Dis, t.Peel, t.Poke, t.Pick, t.Dive, t.Split, t.Mob));
            }
            catch (ArgumentException error)
            {
                // Profilin kendi aralık kontrolleri (güç 1-10, etiket 0-3...): hangi şampiyon olduğu mesaja eklenir.
                throw new InvalidDataException($"{c.Id}: {error.Message}", error);
            }

            entries.Add(new ChampionEntry(id.Value, profile, [.. c.Roles.Select(ParseRole).Distinct()]));
        }

        return entries;
    }

    private static Role ParseRole(string role) => role switch
    {
        "top" => Role.Top,
        "jg" => Role.Jungle,
        "mid" => Role.Mid,
        "adc" => Role.Bot,
        "sup" => Role.Support,
        _ => throw new InvalidDataException($"Bilinmeyen rol: {role} (top, jg, mid, adc, sup)."),
    };

    private static AttackRange ParseRange(string? range) => range switch
    {
        "melee" => AttackRange.Melee,
        "ranged" => AttackRange.Ranged,
        _ => throw new InvalidDataException($"Bilinmeyen menzil: {range} (melee, ranged)."),
    };

    private static ChampionClass ParseClass(string championClass) => championClass switch
    {
        "tank" => ChampionClass.Tank,
        "fighter" => ChampionClass.Fighter,
        "assassin" => ChampionClass.Assassin,
        "mage" => ChampionClass.Mage,
        "marksman" => ChampionClass.Marksman,
        "enchanter" => ChampionClass.Enchanter,
        _ => throw new InvalidDataException(
            $"Bilinmeyen sınıf: {championClass} (tank, fighter, assassin, mage, marksman, enchanter)."),
    };

    private sealed record PoolFile(List<ChampionJson>? Champions);

    // DESIGN.md §16.2 biçimi; tags'te verilmeyen etiket 0.
    private sealed record ChampionJson(
        string? Id, string? Name, List<string>? Roles, string? Range, List<string>? Classes,
        int E, int M, int L, int Econ, double Ap, TagsEntry? Tags);

    private sealed record TagsEntry(
        int Fl = 0, int En = 0, int Dis = 0, int Peel = 0, int Poke = 0, int Pick = 0, int Dive = 0, int Split = 0, int Mob = 0);
}
