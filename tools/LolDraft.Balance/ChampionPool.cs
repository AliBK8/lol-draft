using System.Text.Json;
using LolDraft.Engine;

namespace LolDraft.Balance;

internal enum Role
{
    Top,
    Jungle,
    Mid,
    Bot,
    Support,
}

/// <summary>Şampiyon havuzu: DESIGN.md §3 biçimindeki JSON'dan, rol başına liste.</summary>
internal sealed class ChampionPool
{
    private readonly Dictionary<Role, List<ChampionProfile>> _byRole;

    private ChampionPool(Dictionary<Role, List<ChampionProfile>> byRole) => _byRole = byRole;

    public IReadOnlyList<ChampionProfile> For(Role role) => _byRole[role];

    /// <summary>Her şampiyon bir kez, dosya sırasıyla (çok rollü şampiyon ilk rolünde).</summary>
    public IReadOnlyList<ChampionProfile> All =>
        Enum.GetValues<Role>().SelectMany(For).DistinctBy(c => c.Id).ToList();

    public IReadOnlyCollection<Role> RolesOf(ChampionProfile champion) =>
        Enum.GetValues<Role>().Where(role => For(role).Contains(champion)).ToList();

    public static ChampionPool Load(string path)
    {
        using var file = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<PoolFile>(file, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException($"{path} boş.");

        var byRole = Enum.GetValues<Role>().ToDictionary(r => r, _ => new List<ChampionProfile>());
        foreach (var c in data.Champions)
        {
            if (c.Classes is not { Count: 1 or 2 })
                throw new InvalidDataException($"{c.Id}: 1 ya da 2 sınıf olmalı.");

            var t = c.Tags ?? new TagsEntry();
            var profile = new ChampionProfile(
                c.Id, c.Name, ParseRange(c.Range), ParseClass(c.Classes[0]), c.Classes.Count == 2 ? ParseClass(c.Classes[1]) : null,
                c.E, c.M, c.L, c.Econ, c.Ap,
                new ChampionTags(t.Fl, t.En, t.Dis, t.Peel, t.Poke, t.Pick, t.Dive, t.Split, t.Mob));
            foreach (var role in c.Roles)
                byRole[ParseRole(role)].Add(profile);
        }

        // Ayna draft'ta iki takım aynı rolden farklı şampiyon seçer: her rolde en az 2 şampiyon lazım.
        foreach (var (role, champions) in byRole)
        {
            if (champions.Count < 2)
                throw new InvalidDataException($"{role} rolünde en az 2 şampiyon olmalı ({champions.Count} var).");
        }

        return new ChampionPool(byRole);
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

    private static AttackRange ParseRange(string range) => range switch
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

    private sealed record PoolFile(List<ChampionEntry> Champions);

    // DESIGN.md §16.2 biçimi; tags'te verilmeyen etiket 0.
    private sealed record ChampionEntry(
        string Id, string Name, List<string> Roles, string Range, List<string> Classes,
        int E, int M, int L, int Econ, double Ap, TagsEntry? Tags);

    private sealed record TagsEntry(
        int Fl = 0, int En = 0, int Dis = 0, int Peel = 0, int Poke = 0, int Pick = 0, int Dive = 0, int Split = 0, int Mob = 0);
}
