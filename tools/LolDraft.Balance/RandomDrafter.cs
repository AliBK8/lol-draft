using LolDraft.Engine;

namespace LolDraft.Balance;

/// <summary>
/// Rastgele draft üretir: her rolde iki takıma farklı şampiyon; çok rollü bir şampiyon maçta tek kez seçilir.
/// Araç motor değildir; burada System.Random kullanmak serbest, ama aynı --seed aynı draft dizisini versin diye seed'li.
/// </summary>
internal sealed class RandomDrafter(ChampionPool pool, int seed)
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
        var available = pool.For(role).Where(c => !used.Contains(c.Id)).ToList();
        if (available.Count == 0)
            throw new InvalidOperationException($"{role} rolünde seçilecek şampiyon kalmadı; havuzu genişlet.");

        var champion = available[_random.Next(available.Count)];
        used.Add(champion.Id);
        return champion;
    }
}
