using LolDraft.Engine.Simulation;

namespace LolDraft.Engine;

/// <summary>Simülatör sınıfı internal kalsın diye dışarıya sadece bu fabrika açılır.</summary>
public static class MatchEngine
{
    public static IMatchSimulator CreateSimulator() => new MatchSimulator();

    /// <summary>
    /// Şampiyonun bu yuvadaki rol dışı cezası (0 = kendi rolünde; DESIGN.md §16.8). Arayüz swap'ta gösterir;
    /// tablo motorda tek kaynak kalsın diye istemciye hesaplanmış değer gider.
    /// </summary>
    public static double OffRolePenalty(ChampionProfile champion, ChampionRole slot) => OffRole.Penalty(champion, slot);
}
