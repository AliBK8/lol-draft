using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Balance;

/// <summary>
/// Rastgele draft üretir: her rolde iki takıma farklı şampiyon; çok rollü bir şampiyon maçta tek kez seçilir.
/// Araç motor değildir; burada System.Random kullanmak serbest, ama aynı --seed aynı draft dizisini versin diye seed'li.
/// offRoleRate: yuva bu ihtimalle o rolü oynamayan bir şampiyonla dolar (rol dışı kumar, DESIGN.md §16.8).
/// 0 ise zar hiç atılmaz: rol dışısız raporlar eskisiyle bayt bayt aynı.
/// </summary>
internal sealed class RandomDrafter(ChampionPool pool, int seed, double offRoleRate = 0)
{
    private readonly Random _random = new(seed);

    public (TeamDraft A, TeamDraft B) NextPair()
    {
        var a = new ChampionProfile[5];
        var b = new ChampionProfile[5];
        var used = new HashSet<string>();

        foreach (var role in Enum.GetValues<Role>())
        {
            a[(int)role] = Pick(role, used);
            b[(int)role] = Pick(role, used);
        }

        return (new TeamDraft(a[0], a[1], a[2], a[3], a[4]), new TeamDraft(b[0], b[1], b[2], b[3], b[4]));
    }

    public Seed NextSeed() => new((uint)_random.NextInt64(0, uint.MaxValue + 1L));

    private ChampionProfile Pick(Role role, HashSet<string> used)
    {
        var offRole = offRoleRate > 0 && _random.NextDouble() < offRoleRate;
        var available = offRole
            ? pool.All.Where(c => !used.Contains(c.Id) && !pool.RolesOf(c).Contains(role)).ToList()
            : pool.For(role).Where(c => !used.Contains(c.Id)).ToList();
        if (available.Count == 0)
            throw new InvalidOperationException($"{role} rolünde seçilecek şampiyon kalmadı; havuzu genişlet.");

        var champion = available[_random.Next(available.Count)];
        used.Add(champion.Id);
        return champion;
    }
}
