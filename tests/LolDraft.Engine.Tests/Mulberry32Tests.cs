using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Tests;

/// <summary>Beklenen değerler prototipteki JS mulberry32'den alındı (Node ile, toPrecision(17)).</summary>
public sealed class Mulberry32Tests
{
    [Theory]
    [InlineData(42u, 0.60110375192016363, 0.44829055899754167, 0.85246579349040985)]
    [InlineData(0u, 0.26642920868471265, 0.00032974570058286190, 0.22327202744781971)]
    // 2^31'den büyük seed: JS'te a|0 negatif int'e döner; C# uint ile bitler aynı kalmalı.
    [InlineData(3000000000u, 0.75688752857968211, 0.37332263821735978, 0.97499459213577211)]
    public void Ayni_seed_JS_ile_bit_bit_ayni_sayilari_uretir(uint seed, double first, double second, double third)
    {
        var rng = new Mulberry32(new Seed(seed));

        Assert.Equal(first, rng.NextDouble());
        Assert.Equal(second, rng.NextDouble());
        Assert.Equal(third, rng.NextDouble());
    }

    [Fact]
    public void Normal_sayilar_JS_Box_Muller_ile_ayni()
    {
        var rng = new Mulberry32(new Seed(42));

        // Math.Log/Cos son basamakta platforma göre değişebilir; 12 basamak yeterli.
        Assert.Equal(-0.95616222938414897, rng.NextNormal(), 12);
        Assert.Equal(-0.27302610488261042, rng.NextNormal(), 12);
    }

    [Fact]
    public void Sayilar_0_ile_1_arasinda()
    {
        var rng = new Mulberry32(new Seed(7));

        for (var i = 0; i < 100_000; i++)
        {
            var x = rng.NextDouble();
            Assert.True(x is >= 0 and < 1, $"{x} [0, 1) dışında");
        }
    }
}
