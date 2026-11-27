namespace LolDraft.Engine.Randomness;

/// <summary>Motorun alt sistemleri; her birinin kendi zar akışı var (DESIGN.md §4.1). Sıra değişmez: numara seed'e girer.</summary>
internal enum RandomStream
{
    Lanes,
    Jungle,
    Invade,
    Skirmish,
    Objective,
    Split,
    Siege,
    Catch,
    Towers,
    Layout,
}

/// <summary>
/// Maç seed'inden alt sistem başına bağımsız zar akışları (DESIGN.md §4.1). Bir alt sisteme zar eklemek ya da bir karar
/// diğer alt sistemlerin zarlarını kaydırmaz: koridor gürültüsü, gank'a yeni bir zar eklense de aynı kalır.
/// </summary>
internal sealed class RandomStreams
{
    private readonly IRandomSource[] _streams;

    public RandomStreams(Seed seed) =>
        _streams = Enum.GetValues<RandomStream>().Select(s => (IRandomSource)new Mulberry32(new Seed(Derive(seed.Value, (uint)s)))).ToArray();

    public IRandomSource this[RandomStream stream] => _streams[(int)stream];

    /// <summary>
    /// Alt seed: seed'e akış numarasıyla altın oran adımı (0x9E3779B9) eklenir, sonra murmur3'ün son karıştırıcısı
    /// (fmix32) bitleri dağıtır. Yakın seed'ler (5, 6) ve yakın akışlar (0, 1) birbirinden uzak alt seed'ler verir;
    /// mulberry32 yakın başlangıç durumlarında da bağımsız dizi üretir ama karıştırma güvence.
    /// </summary>
    internal static uint Derive(uint seed, uint stream)
    {
        unchecked
        {
            var h = seed + (stream + 1) * 0x9E3779B9u;
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }
}
