using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

public sealed class ProbabilityTests
{
    [Fact]
    public void Lojistik_esit_farkta_yuzde_50_ve_simetrik()
    {
        Assert.Equal(0.5, Probability.Logistic(0));
        Assert.Equal(1 - Probability.Logistic(2.25), Probability.Logistic(-2.25), 12);
    }

    [Theory]
    // Gank: ormancı farkı 9 → σ(9/4) ≈ 0.905 → 0.9
    [InlineData(9 / 4.0, 0.9)]
    [InlineData(-9 / 4.0, 0.1)]
    [InlineData(0, 0.5)]
    public void Olay_ihtimali_yuzde_10_ile_90_arasina_sikistirilir(double x, double expected)
    {
        Assert.Equal(expected, Probability.Clamp(Probability.Logistic(x)), 12);
    }
}
