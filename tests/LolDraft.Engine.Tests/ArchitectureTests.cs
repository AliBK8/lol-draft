using System.Reflection;

namespace LolDraft.Engine.Tests;

// Engine saf kütüphane: başka katmanı, ASP.NET'i ya da EF'i bilmez (DESIGN.md §8.2)
public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden = ["LolDraft.", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore"];

    [Fact]
    public void Engine_yasak_bagimlilik_kullanmiyor()
    {
        var references = Assembly.Load("LolDraft.Engine").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => Forbidden.Any(name.StartsWith));
    }

    // Dışarıya sadece sözleşme açık (DESIGN.md §8.3). Yeni public tip eklemek bilinçli bir karar olmalı: listeyi güncelle.
    [Fact]
    public void Engine_disariya_sadece_sozlesmeyi_acar()
    {
        string[] allowed =
        [
            "IMatchSimulator", "MatchEngine", "MatchInput", "MatchResult", "MatchEnd", "MatchEndKind",
            "ScoreBreakdown", "ScoreCategory", "Seed", "TeamSide", "Lane", "ChampionProfile", "ChampionTags", "ChampionClass",
            "AttackRange", "TeamDraft",
            "MatchEvent", "Objective", "ObjectiveEvent", "KillEvent", "SiegeEvent", "NexusEvent",
            "TowerEvent", "PlateEvent", "CsEvent", "SurpriseEvent", "GankFailEvent", "MoveEvent",
            "SkirmishEvent", "SplitEvent", "RiverSide", "ThrowEvent", "DragonSoulEvent", "GoldFrame", "ChampionGold",
            "DraftReport", "RuleImpact", "RuleContext", "CarryReport", "TeamGoldReport",
            "ChampionRole", "OffRoleReport",
        ];

        var exported = typeof(IMatchSimulator).Assembly.GetExportedTypes().Select(t => t.Name);

        Assert.Equal(allowed.Order(), exported.Order());
    }
}
