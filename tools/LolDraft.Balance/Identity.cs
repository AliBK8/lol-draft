using LolDraft.Engine;

namespace LolDraft.Balance;

internal enum Identity
{
    Engage,
    Poke,
    Pick,
    Protect,
    Dive,
    Split,
    Mixed,
}

/// <summary>
/// Takım kimliği (DESIGN.md §16.12): draft'ın en belirgin etiketi. Ham toplam yanıltır (havuzda en bol, poke az); her
/// etiketin takım toplamı havuzdaki beklenen takım toplamına bölünür. En yüksek oran ≥ 1,25 ise kimlik o, değilse karma.
/// Örnek: beklenen en toplamı 6, takımınki 9 → 1,5; poke beklenen 4, takımınki 4 → 1,0 → Engage.
/// </summary>
internal sealed class IdentityClassifier
{
    private const double Threshold = 1.25;

    private static readonly (Identity Identity, Func<ChampionProfile, int> Tag)[] Tags =
    [
        (Identity.Engage, c => c.Tags.Engage),
        (Identity.Poke, c => c.Tags.Poke),
        (Identity.Pick, c => c.Tags.Pick),
        (Identity.Protect, c => c.Tags.Peel + c.Tags.Disengage),
        (Identity.Dive, c => c.Tags.Dive),
        (Identity.Split, c => c.Tags.Split),
    ];

    // Beklenen takım toplamı: her rolde havuz ortalaması, roller toplanır.
    private readonly double[] _expected;

    public IdentityClassifier(ChampionPool pool) =>
        _expected = Tags.Select(t => Enum.GetValues<Role>().Sum(role => pool.For(role).Average(t.Tag))).ToArray();

    public Identity Of(TeamDraft team)
    {
        var (best, ratio) = (Identity.Mixed, 0.0);
        for (var i = 0; i < Tags.Length; i++)
        {
            var r = _expected[i] == 0 ? 0 : team.Champions.Sum(Tags[i].Tag) / _expected[i];
            if (r > ratio)
                (best, ratio) = (Tags[i].Identity, r);
        }

        return ratio >= Threshold ? best : Identity.Mixed;
    }

    public static string Name(Identity identity) => identity switch
    {
        Identity.Engage => "Engage",
        Identity.Poke => "Poke",
        Identity.Pick => "Pick",
        Identity.Protect => "Koruma",
        Identity.Dive => "Dive",
        Identity.Split => "Split",
        _ => "Karma",
    };
}
