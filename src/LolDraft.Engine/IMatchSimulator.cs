namespace LolDraft.Engine;

/// <summary>Motorun tek giriş kapısı. Aynı girdi her zaman aynı sonucu üretir.</summary>
public interface IMatchSimulator
{
    MatchResult Simulate(MatchInput input);
}
