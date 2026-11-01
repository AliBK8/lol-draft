namespace LolDraft.Engine.Randomness;

internal static class RandomSourceExtensions
{
    /// <summary>[0, count) aralığında rastgele indeks. JS: Math.floor(rng() * count).</summary>
    public static int NextIndex(this IRandomSource rng, int count) => (int)(rng.NextDouble() * count);
}
