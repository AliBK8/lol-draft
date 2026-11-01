using LolDraft.Engine.Simulation;

namespace LolDraft.Engine;

/// <summary>Simülatör sınıfı internal kalsın diye dışarıya sadece bu fabrika açılır.</summary>
public static class MatchEngine
{
    public static IMatchSimulator CreateSimulator() => new MatchSimulator();
}
