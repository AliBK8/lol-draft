using LolDraft.Engine.Events;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Altın kaynakları ve dakika kareleri (DESIGN.md §16.4). Bu dosya zar atmaz; altını defterden okuyan
/// gerçek güç (<c>MatchRun.Power.cs</c>, §16.5) üzerinden savaş ve koridor ihtimallerine girer.
/// </summary>
internal sealed partial class MatchRun
{
    private const double KillGold = 300;
    private const double AssistGold = 150;
    private const double PlateGold = 160;
    private const double TowerLaneGold = 250;
    private const double TowerTeamGold = 50;

    private readonly GoldLedger _gold = new();
    private readonly List<GoldFrame> _goldFrames = [];

    /// <summary>Testler için: maç sonundaki defter.</summary>
    internal GoldLedger Gold => _gold;

    // Shutdown ödülü (DESIGN.md §16.4):
    //   taban = 0.3 × (kurbanın altını − aynı roldeki rakibin altını)
    //   ödül  = clamp(taban × (1 + 0.13 × son ölümünden beri yaşadığı dakika), 0, 700)
    // Örnek: rakibinden 1.000 önde → taban 300; 5 dk ölmezse ~495. Zaman çarpandır: 50 önde olanın ödülü 15 → 25,
    // küçük fark küçük kalır (toplama olsaydı her öndeki tam zaman bonusu alıyordu). Geride olanın ödülü yok.
    // Önde olanı öldürmek farkı kapatır: geri dönüşün ana yolu. Uzun süre ölmeden önde kalan giderek değerli hedef olur.
    private const double BountyShare = 0.3;
    private const double BountyGrowthPerMinute = 0.13;
    private const double MaxBounty = 700;

    // Bu ödülden itibaren kill "Shutdown!" olarak anlatılır.
    private const int ShutdownToNarrate = 150;

    private int KillBounty(ChampionProfile victim, double t) =>
        Bounty(_gold.GoldAt(victim, t), _gold.GoldAt(Counterpart(victim), t), t - _gold.AliveSince(victim));

    internal static int Bounty(double victimGold, double counterpartGold, double secondsAlive)
    {
        var lead = BountyShare * (victimGold - counterpartGold);
        if (lead <= 0)
            return 0;
        return (int)Math.Floor(Math.Min(lead * (1 + BountyGrowthPerMinute * secondsAlive / 60), MaxBounty));
    }

    // Katile 300 + ödül; asist 150, katil dışındaki yaşayan katılımcılara eşit bölünür (kimse yoksa dağıtılmaz).
    private void GiveKillGold(ChampionProfile killer, IReadOnlyCollection<ChampionProfile> assists, int bounty)
    {
        _gold.Earn(killer, GoldSource.Kill, KillGold);
        if (bounty > 0)
            _gold.Earn(killer, GoldSource.Bounty, bounty);
        foreach (var assist in assists)
            _gold.Earn(assist, GoldSource.Assist, AssistGold / assists.Count);
    }

    // Kule: 250 koridorculara eşit bölünür (bot'ta adc ve support 125'er), takımın kalanına kişi başı 50.
    private void GiveTowerGold(TeamSide side, Lane lane)
    {
        var laners = LaneChampions(side, lane);
        foreach (var champion in Team(side).Champions)
        {
            var amount = laners.Contains(champion) ? TowerLaneGold / laners.Length : TowerTeamGold;
            _gold.Earn(champion, GoldSource.Tower, amount);
        }
    }

    // Objektif altını takımın beşine de verilir (ölüler dahil).
    private void GiveObjectiveGold(TeamSide side, Objective objective)
    {
        var amount = ObjectiveGold(objective);
        if (amount == 0)
            return;
        foreach (var champion in Team(side).Champions)
            _gold.Earn(champion, GoldSource.Objective, amount);
    }

    // Kişi başı. Ejder asıl değerini ruhla verir; Elder altın vermez, buff'ı verir (faz A adım 6).
    private static double ObjectiveGold(Objective objective) => objective switch
    {
        Objective.Dragon => 40,
        Objective.Herald => 100,
        Objective.Baron => 300,
        Objective.Elder => 0,
        Objective.FinalFight => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };

    private void RecordGoldFrame(int minute, double t)
    {
        ChampionGold[] champions =
        [
            .. _blue.Champions.Select(c => Snapshot(c, TeamSide.Blue, t)),
            .. _red.Champions.Select(c => Snapshot(c, TeamSide.Red, t)),
        ];
        _goldFrames.Add(new GoldFrame(minute, champions));
    }

    private ChampionGold Snapshot(ChampionProfile champion, TeamSide side, double t) =>
        new(champion.Id, side, (int)Math.Floor(_gold.GoldAt(champion, t)), (int)Math.Floor(_gold.Cs(champion)));
}
