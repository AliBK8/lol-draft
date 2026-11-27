using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Tests;

/// <summary>Alt sistem başına zar akışları (DESIGN.md §4.1).</summary>
public sealed class RandomStreamsTests
{
    [Fact]
    public void Ayni_seed_ayni_akislari_verir()
    {
        var a = new RandomStreams(new Seed(5));
        var b = new RandomStreams(new Seed(5));

        foreach (var stream in Enum.GetValues<RandomStream>())
            Assert.Equal(a[stream].NextDouble(), b[stream].NextDouble());
    }

    [Fact]
    public void Bir_akistan_cekmek_digerini_kaydirmaz()
    {
        var quiet = new RandomStreams(new Seed(5));
        var busy = new RandomStreams(new Seed(5));

        // Örnek: orman akışına fazladan 1.000 zar (yeni bir kural ya da karar); koridor akışı yerinden oynamaz.
        for (var i = 0; i < 1000; i++)
            busy[RandomStream.Jungle].NextDouble();

        Assert.Equal(quiet[RandomStream.Lanes].NextDouble(), busy[RandomStream.Lanes].NextDouble());
    }

    [Fact]
    public void Yakin_seedler_ve_akislar_farkli_alt_seed_verir()
    {
        // Seed 0-99 × bütün akışlar: alt seed'lerin hiçbiri çakışmaz.
        var derived = new HashSet<uint>();
        var count = 0;
        for (uint seed = 0; seed < 100; seed++)
        {
            foreach (var stream in Enum.GetValues<RandomStream>())
            {
                derived.Add(RandomStreams.Derive(seed, (uint)stream));
                count++;
            }
        }

        Assert.Equal(count, derived.Count);
    }
}
