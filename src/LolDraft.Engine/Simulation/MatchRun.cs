using LolDraft.Engine.Events;
using LolDraft.Engine.Randomness;

namespace LolDraft.Engine.Simulation;

/// <summary>
/// Tek bir maçın durumu ve dakika döngüsü (DESIGN.md §4.5). Kurallar ayrı dosyalarda (partial):
/// <c>MatchRun.Lanes.cs</c> koridor akışı, sürpriz ve anlatıcı; <c>MatchRun.Jungle.cs</c> orman ve gank;
/// <c>MatchRun.Skirmish.cs</c> nehir çatışması; <c>MatchRun.Objectives.cs</c> objektifler ve son savaş;
/// <c>MatchRun.Siege.cs</c> kuşatma ve throw.
/// </summary>
internal sealed partial class MatchRun
{
    private readonly TeamDraft _blue;
    private readonly TeamDraft _red;
    private readonly IRandomSource _rng;
    private readonly ScoreSheet _scores = new();
    private readonly List<MatchEvent> _events = [];
    private readonly DeathTracker _deaths = new();
    private readonly int _synergyDiff;

    public MatchRun(MatchInput input, IRandomSource rng)
    {
        _blue = input.Blue;
        _red = input.Red;
        _rng = rng;
        // sinerji = sinerji(Mavi) − sinerji(Kırmızı) (DESIGN.md §4.4)
        _synergyDiff = SynergyCalculator.Score(_blue) - SynergyCalculator.Score(_red);
    }

    public MatchResult Play()
    {
        _scores.Add(ScoreCategory.Synergy, _synergyDiff);

        // Her dakika sırası: koridorlar → orman → nehir → objektif → kuşatma. Maçı objektif (dk 35) ya da kuşatma bitirir.
        MatchEnd? end = null;
        for (var minute = 1; end is null; minute++)
        {
            PlayLanes(minute);
            PlayJungle(minute);
            PlaySkirmish(minute);
            end = PlayObjective(minute) ?? PlaySiege(minute);
        }

        // Olaylar kural sırasıyla üretilir (önce koridorlar, sonra savaş...), zaman sırasıyla değil.
        // OrderBy kararlıdır: aynı saniyedeki olaylar üretildikleri sırada kalır (JS Array.sort ile aynı).
        // Nexus'tan sonraya düşen olaylar (ör. o dakikanın 55. sn'sindeki CS) atılır (DESIGN.md §4.6).
        var ordered = _events.OrderBy(e => e.T).ToList();
        var nexusT = ordered.OfType<NexusEvent>().First().T;
        return new MatchResult(ordered.Where(e => e.T <= nexusT).ToArray(), _scores.ToBreakdown(), end);
    }

    private void Kill(TeamSide side, ChampionProfile killer, ChampionProfile victim, double t, string text, Lane? lane = null)
    {
        var until = _deaths.Kill(victim, t);
        // Maçın ilk kill'i "İlk kan!" ön ekiyle yazılır (DESIGN.md §4.7).
        var prefix = _deaths.Kills == 1 ? "İlk kan! " : "";
        _events.Add(new KillEvent(t, prefix + text, side, killer.Id, victim.Id, until, lane));
    }

    // Sıra takım sırasıdır (top, jungle, mid, bot, support); zarla seçilen indeksler bu sıraya göre.
    private List<ChampionProfile> AliveAt(TeamSide side, double t) =>
        Team(side).Champions.Where(c => _deaths.IsAlive(c, t)).ToList();

    private TeamDraft Team(TeamSide side) => side == TeamSide.Blue ? _blue : _red;
}
