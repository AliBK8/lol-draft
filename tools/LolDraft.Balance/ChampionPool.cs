using LolDraft.Domain;
using LolDraft.Engine;
using LolDraft.Infrastructure.Champions;

namespace LolDraft.Balance;

/// <summary>Şampiyon havuzu: oyunla aynı okuyucudan (<see cref="ChampionFile"/>), rol başına liste.</summary>
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

        var byRole = Enum.GetValues<Role>().ToDictionary(r => r, _ => new List<ChampionProfile>());
        foreach (var champion in ChampionFile.Parse(file))
        {
            foreach (var role in champion.Roles)
                byRole[role].Add(champion.Profile);
        }

        // Ayna draft'ta iki takım aynı rolden farklı şampiyon seçer: her rolde en az 2 şampiyon lazım.
        foreach (var (role, champions) in byRole)
        {
            if (champions.Count < 2)
                throw new InvalidDataException($"{role} rolünde en az 2 şampiyon olmalı ({champions.Count} var).");
        }

        return new ChampionPool(byRole);
    }
}
