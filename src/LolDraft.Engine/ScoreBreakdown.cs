namespace LolDraft.Engine;

/// <summary>Maç sonu puan dökümü. Pozitif değer Mavi lehine.</summary>
public sealed class ScoreBreakdown
{
    private readonly double[] _values;

    internal ScoreBreakdown(double[] values) => _values = (double[])values.Clone();

    public double this[ScoreCategory category] => _values[(int)category];

    public double Total => Simulation.ScoreSheet.SumInOrder(_values);
}
