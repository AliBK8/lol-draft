namespace LolDraft.Engine.Simulation;

/// <summary>Maç sırasında biriken puanlar. Pozitif değer Mavi lehine.</summary>
internal sealed class ScoreSheet
{
    private readonly double[] _values = new double[Enum.GetValues<ScoreCategory>().Length];

    public double Total => SumInOrder(_values);

    // Kategori sırasıyla soldan sağa toplar (JS reduce ile aynı). Toplama sırası double'da son basamağı değiştirir;
    // LINQ Sum'ın vektörleştirilmiş sırasına güvenmiyoruz.
    internal static double SumInOrder(double[] values)
    {
        var total = 0.0;
        foreach (var value in values)
            total += value;
        return total;
    }

    public void Add(ScoreCategory category, double value) => _values[(int)category] += value;

    public ScoreBreakdown ToBreakdown() => new(_values);
}
