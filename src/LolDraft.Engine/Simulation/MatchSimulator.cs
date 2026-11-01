using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>Durumsuz; her maç kendi <see cref="MatchRun"/> nesnesinde oynanır, böylece tek örnek paylaşılabilir.</summary>
internal sealed class MatchSimulator : IMatchSimulator
{
    public MatchResult Simulate(MatchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new MatchRun(input, new Mulberry32(input.Seed)).Play();
    }
}
