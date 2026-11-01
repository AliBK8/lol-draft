namespace LolDraft.Engine.Simulation;

/// <summary>Kim ne zamana kadar ölü (DESIGN.md §4.7).</summary>
internal sealed class DeathTracker
{
    // Sadece arama için; üzerinde döngü kurulmadığı için Dictionary sırası deterministikliği etkilemez.
    private readonly Dictionary<string, double> _deadUntil = [];

    public int Kills { get; private set; }

    // Ölü değilse yaşıyor: yeniden doğma anına eşit ya da sonrasıysa yaşıyor sayılır (JS: !(dead[id] > t)).
    public bool IsAlive(ChampionProfile champion, double t) =>
        !_deadUntil.TryGetValue(champion.Id, out var until) || until <= t;

    /// <summary>Kurbanı öldürür ve yeniden doğma anını döner.</summary>
    public double Kill(ChampionProfile victim, double t)
    {
        // Yeniden doğma süresi: 10 + t/60 saniye (t = oyun saniyesi). 10. dk'da 20 sn, 30. dk'da 40 sn.
        // Parantez JS ile aynı: (t + 10) + t/60 double'da son basamakta farklı çıkabilir.
        var until = t + (10 + t / 60);
        _deadUntil[victim.Id] = until;
        Kills++;
        return until;
    }
}
