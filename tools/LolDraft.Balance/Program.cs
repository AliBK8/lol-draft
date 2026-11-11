// Denge aracı (DESIGN.md §9): motoru rastgele draft'larla çalıştırıp hedeflerle karşılaştırır.
// Her draft ayna oynanır (A Mavi/B Kırmızı, sonra yer değiştirip aynı seed): draft etkisi iki tarafa eşit dağılır,
// Mavi'nin %50'den sapması saf taraf etkisidir.
using System.Text;
using LolDraft.Balance;
using LolDraft.Engine;

// Çıktı dosyaya/boruya yönlendirilince de Türkçe karakterler bozulmasın.
Console.OutputEncoding = Encoding.UTF8;

var options = BalanceOptions.Parse(args);
var pool = ChampionPool.Load(options.ChampionsPath);
if (options.Check)
{
    PoolCheck.Print(pool, Console.Out);
    return;
}

var drafter = new RandomDrafter(pool, options.Seed, options.OffRoleRate);
var simulator = MatchEngine.CreateSimulator();
var stats = new BalanceStats(new IdentityClassifier(pool));

for (var i = 0; i < options.Drafts; i++)
{
    var (a, b) = drafter.NextPair();
    var seed = drafter.NextSeed();

    stats.Add(a, b, simulator.Simulate(new MatchInput(a, b, seed)), options.PowerGapBucket, options.EarlyCompThreshold);
    stats.Add(b, a, simulator.Simulate(new MatchInput(b, a, seed)), options.PowerGapBucket, options.EarlyCompThreshold);
}

BalanceReport.Print(stats, options, Console.Out);
