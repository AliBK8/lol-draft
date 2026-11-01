namespace LolDraft.Engine.Simulation;

internal static class Probability
{
    public const double Min = 0.1;
    public const double Max = 0.9;

    // Lojistik fonksiyon σ(x) = 1 / (1 + e^(−x)): x = 0 → %50; x büyüdükçe 1'e, küçüldükçe 0'a yaklaşır.
    public static double Logistic(double x) => 1 / (1 + Math.Exp(-x));

    // Olay ihtimalleri %10-%90 arasında sınırlı (CLAUDE.md temel kural): hiçbir sonuç kesin değil.
    public static double Clamp(double p) => Math.Clamp(p, Min, Max);
}
