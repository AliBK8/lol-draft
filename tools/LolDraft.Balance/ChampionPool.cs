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

    public static ChampionPool Load(string path)
    {
        using var file = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<PoolFile>(file, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException($"{path} boş.");

        var byRole = Enum.GetValues<Role>().ToDictionary(r => r, _ => new List<ChampionProfile>());
        foreach (var c in data.Champions)
        {
            var profile = new ChampionProfile(c.Id, c.Name, c.E, c.M, c.L, c.Fl, c.En, c.Ap);
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

    private sealed record PoolFile(List<ChampionEntry> Champions);

    private sealed record ChampionEntry(string Id, string Name, List<string> Roles, int E, int M, int L, int Fl, int En, double Ap);
}
