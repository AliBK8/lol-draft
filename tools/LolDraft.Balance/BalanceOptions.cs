namespace LolDraft.Balance;

/// <summary>
/// Komut satırı: --drafts 10000 --seed 1 --champions havuz.json; --check sadece havuz kontrolü;
/// --off-role 0.1: her yuva %10 ihtimalle başka rolün şampiyonuyla dolar (rol dışı kumarı ölçmek için; varsayılan 0).
/// </summary>
internal sealed record BalanceOptions(
    int Drafts, int Seed, string ChampionsPath, int PowerGapBucket, int EarlyCompThreshold, bool Check, double OffRoleRate)
{
    public static BalanceOptions Parse(string[] args)
    {
        var drafts = 10_000;
        var seed = 1;
        var champions = Path.Combine(AppContext.BaseDirectory, "champions.json");
        var powerGapBucket = 5;
        var earlyCompThreshold = 15;
        var check = false;
        var offRole = 0.0;

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
                case "--check": check = true; break;
                case "--off-role": offRole = double.Parse(Value(), System.Globalization.CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Bilinmeyen seçenek: {args[i]}");
            }
        }

        if (offRole is < 0 or > 1)
            throw new ArgumentException("--off-role 0 ile 1 arasında olmalı.");

        return new BalanceOptions(drafts, seed, champions, powerGapBucket, earlyCompThreshold, check, offRole);
    }
}
