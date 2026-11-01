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
    // Savaş kill'leri arası süre (sn).
    private const double KillInterval = 1.5;

    private readonly TeamDraft _blue;
    private readonly TeamDraft _red;
    private readonly IRandomSource _rng;
    private readonly ScoreSheet _scores = new();
    private readonly List<MatchEvent> _events = [];
    private readonly DeathTracker _deaths = new();
    private readonly int _synergyDiff;

    // Zaman çizelgesi: dakikanın işleri saniye sırasıyla çözülür. Eşit saniyede kayıt sırası (Seq) belirler,
    // böylece sıra her çalıştırmada aynıdır (deterministik).
    private readonly PriorityQueue<Action, (double T, long Seq)> _timeline = new();
    private long _nextSeq;
    private MatchEnd? _end;

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

        for (var minute = 1; _end is null; minute++)
        {
            // 1) Kayıt: sabit akışlar (koridor, orman) dakika başında puana yazılır; olaylar saniyesine kaydolur.
            ScheduleLanes(minute);
            ScheduleJungle(minute);
            ScheduleSkirmish(minute);
            ScheduleObjective(minute);
            ScheduleSiege(minute);

            // 2) Çözüm: saniye sırasıyla. Her iş, kendi saniyesindeki duruma (kim yaşıyor, toplam puan) bakar.
            // Nexus düşerse kalan işler iptal edilir.
            while (_end is null && _timeline.TryDequeue(out var job, out _))
                job();
            _timeline.Clear();
        }

        // Olaylar çözüm sırasıyla, yani zaman sırasıyla eklenir; OrderBy (kararlı) güvence için.
        var ordered = _events.OrderBy(e => e.T).ToList();
        var nexusT = ordered.OfType<NexusEvent>().First().T;
        return new MatchResult(ordered.Where(e => e.T <= nexusT).ToArray(), _scores.ToBreakdown(), _end);
    }

    /// <summary>İşi zaman çizelgesine yazar; dakika içinde saniyesi gelince çözülür.</summary>
    private void At(double t, Action job) => _timeline.Enqueue(job, (t, _nextSeq++));

    private void EndMatch(MatchEnd end) => _end = end;

    /// <summary>
    /// Bir savaşın kill'lerini kendi saniyelerine kaydeder: firstT, firstT + 1.5, ...
    /// Her kill kendi anında yaşayan kurban ve katil arasından seçilir; aradaki olaylarda kurbanlar öldüyse kill düşer.
    /// Zar sırası (her kill için): önce kurban, sonra katil.
    /// </summary>
    private void ScheduleKills(TeamSide winner, IReadOnlyList<ChampionProfile> winners, IReadOnlyList<ChampionProfile> losers,
        int count, double firstT, Func<ChampionProfile, ChampionProfile, string> text)
    {
        var remaining = losers.ToList();

        for (var i = 0; i < count; i++)
        {
            var t = firstT + i * KillInterval;
            At(t, () =>
            {
                var victims = remaining.Where(c => _deaths.IsAlive(c, t)).ToList();
                var killers = winners.Where(c => _deaths.IsAlive(c, t)).ToList();
                if (victims.Count == 0 || killers.Count == 0)
                    return;

                var victim = victims[_rng.NextIndex(victims.Count)];
                remaining.Remove(victim);
                var killer = killers[_rng.NextIndex(killers.Count)];
                Kill(winner, killer, victim, t, text(killer, victim));
            });
        }
    }

    private void Kill(TeamSide side, ChampionProfile killer, ChampionProfile victim, double t, string text, Lane? lane = null)
    {
        var until = _deaths.Kill(victim, t);
        // Maçın ilk kill'i "İlk kan!" ön ekiyle yazılır (DESIGN.md §4.7). Kill'ler zaman sırasıyla çözüldüğü için
        // işlenen ilk kill zaman çizelgesinde de ilktir.
        var prefix = _deaths.Kills == 1 ? "İlk kan! " : "";
        _events.Add(new KillEvent(t, prefix + text, side, killer.Id, victim.Id, until, lane));
    }

    // Sıra takım sırasıdır (top, jungle, mid, bot, support); zarla seçilen indeksler bu sıraya göre.
    private List<ChampionProfile> AliveAt(TeamSide side, double t) =>
        Team(side).Champions.Where(c => _deaths.IsAlive(c, t)).ToList();

    private TeamDraft Team(TeamSide side) => side == TeamSide.Blue ? _blue : _red;
}
