namespace LolDraft.Balance;

/// <summary>Komut satırı: --drafts 10000 --seed 1 --champions havuz.json</summary>
internal sealed record BalanceOptions(int Drafts, int Seed, string ChampionsPath, int PowerGapBucket, int EarlyCompThreshold)
{
    public static BalanceOptions Parse(string[] args)
    {
        var drafts = 10_000;
        var seed = 1;
        var champions = Path.Combine(AppContext.BaseDirectory, "sample-champions.json");
        var powerGapBucket = 5;
        var earlyCompThreshold = 15;

        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} için değer eksik.");

            switch (args[i])
            {
                case "--drafts": drafts = int.Parse(Value()); break;
                case "--seed": seed = int.Parse(Value()); break;
                case "--champions": champions = Value(); break;
                case "--gap-bucket": powerGapBucket = int.Parse(Value()); break;
                case "--early-threshold": earlyCompThreshold = int.Parse(Value()); break;
                default: throw new ArgumentException($"Bilinmeyen seçenek: {args[i]}");
            }
        }

        return new BalanceOptions(drafts, seed, champions, powerGapBucket, earlyCompThreshold);
    }
}
