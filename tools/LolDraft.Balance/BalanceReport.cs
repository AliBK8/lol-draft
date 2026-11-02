using System.Globalization;
using LolDraft.Engine;

namespace LolDraft.Balance;

/// <summary>Biriken sayıları DESIGN.md §9 hedefleriyle yan yana yazar.</summary>
internal static class BalanceReport
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static void Print(BalanceStats s, BalanceOptions o, TextWriter w)
    {
        w.WriteLine($"Denge raporu — {s.Matches} maç ({s.Matches / 2} draft × ayna), draft seed {o.Seed}, havuz {Path.GetFileName(o.ChampionsPath)}");
        w.WriteLine();

        w.WriteLine("## Genel");
        Row(w, "Mavi kazanma (ayna: draft etkisi yok, saf taraf etkisi)", Pct(s.BlueWins, s.Matches) + Margin(s.BlueWins, s.Matches), "~%50");
        Row(w, "Ortalama süre", $"{s.MinuteSum / s.Matches:0.0} dk", "25-32 dk");
        Row(w, "Kuşatmayla 35'ten önce biten", Pct(s.SiegeEnds, s.Matches), "—");
        Row(w, "Maç başı kill", $"{(double)s.Kills / s.Matches:0.0}", "15-30");
        Row(w, "Maç başı kule", $"{(double)s.Towers / s.Matches:0.0}", "—");
        Row(w, "Maç başı ejder / Elder", $"{(double)s.Dragons / s.Matches:0.0} / {(double)s.Elders / s.Matches:0.00}", "—");
        Row(w, "Maç başı throw", $"{(double)s.Throws / s.Matches:0.00}", "—");
        Row(w, "Ruh alınan maç / ruhu alan kazanıyor", $"{Pct(s.SoulMatches, s.Matches)} / {Pct(s.SoulWins, s.SoulMatches)}", "— / %65-75");
        Row(w, "Sınıra (%10/%90) takılan savaş", $"{Pct(s.ClampedFights, s.Fights)} ({s.ClampedFights}/{s.Fights})", "düşük");
        w.WriteLine();

        w.WriteLine("## Ekonomi");
        Row(w, "14. dk ortalama CS (koridorcu + ormancı)", $"{s.LanerCsAt14 / Math.Max(1, s.LanerCsSamples):0.0}", "—");
        Row(w, "15. dk ortalama takım altın farkı", $"{s.TeamLeadAt15 / Math.Max(1, s.TeamLeadSamples):0}", "—");
        Row(w, "Maç başı shutdown (150+) / ortalama ödül",
            $"{(double)s.Shutdowns / s.Matches:0.00} / {s.ShutdownGold / Math.Max(1, s.Shutdowns):0}", "—");
        Row(w, "Maç başı yakalanma / ortalama ödül",
            $"{(double)s.Catches / s.Matches:0.00} / {s.CatchGold / Math.Max(1, s.Catches):0}", "—");
        Row(w, "Maç başı split / cevaplanan / kule getiren",
            $"{(double)s.Splits / s.Matches:0.00} / {Pct(s.SplitsAnswered, s.Splits)} / {Pct(s.SplitTowers, s.Splits)}", "—");
        Row(w, "Split yapan objektifi alıyor / maçı kazanıyor",
            $"{Pct(s.SplitObjectiveWins, s.Splits)} / {Pct(s.SplitMatchWins, s.Splits)}", "—");
        w.WriteLine();

        w.WriteLine("## Altın farkı → kazanma (o dakikada altında önde olan kazanıyor; o dakikaya ulaşan maçlar)");
        w.WriteLine($"  {"fark",-12}" + string.Concat(BalanceStats.CurveMinutes.Select(m => $"{m + ". dk",-26}")));
        for (var i = 0; i < BalanceStats.LeadBuckets.Length; i++)
        {
            var from = BalanceStats.LeadBuckets[i];
            var label = i + 1 < BalanceStats.LeadBuckets.Length ? $"{from}-{BalanceStats.LeadBuckets[i + 1] - 1}" : $"{from}+";
            var cells = BalanceStats.CurveMinutes.Select(m =>
            {
                var (games, wins) = s.ByGoldLead.GetValueOrDefault((m, from));
                return $"{Pct(wins, games) + Margin(wins, games) + $" ({Pct(games, s.Matches)})",-26}";
            });
            w.WriteLine($"  {label,-12}" + string.Concat(cells));
        }
        w.WriteLine("  (parantez: o dilimdeki maçların tüm maçlara oranı) hedef: 15. dk 3.000+ önde → %70-80");
        w.WriteLine();

        w.WriteLine("## Daha güçlü draft ne kadar kazanıyor? (güç = Σ erken+orta+geç)");
        foreach (var (bucket, (games, wins)) in s.ByPowerGap)
            Row(w, $"güç farkı {bucket}-{bucket + o.PowerGapBucket - 1}", $"{Pct(wins, games)}{Margin(wins, games)}  ({games} maç)", "");
        w.WriteLine();

        w.WriteLine("## Kuralları daha iyi draft ne kadar kazanıyor? (son savaştaki takım kuralları farkı, K5-K11, K13)");
        foreach (var (gap, (games, wins)) in s.BySynergyGap)
            Row(w, $"kural farkı {gap}", $"{Pct(wins, games)}{Margin(wins, games)}  ({games} maç)", "");
        w.WriteLine("  hedef: belirgin iyi draft tek maçta %60-65");
        w.WriteLine();

        w.WriteLine("## Kurallar (çalıştığı maç · ortalama doğrudan etki · net lehine olan taraf kazanıyor)");
        foreach (var ((number, context), (matches, decided, wins, impact)) in s.Rules)
        {
            var lane = context == RuleContext.TopLane;
            var effect = lane ? $"{impact / Math.Max(1, decided):0} altın" : $"{impact / Math.Max(1, decided):0.0} puan";
            Row(w, $"K{number} {ContextName(context)}",
                $"{Pct(matches, s.Matches),6} maç · {effect,-10} · {Pct(wins, decided)}{Margin(wins, decided)}", "");
        }
        w.WriteLine("  koridor: farm altını farkına maç başı katkı; savaş: olay başına kazanma şansı (yüzde puan)");
        w.WriteLine();

        w.WriteLine("## Kimlikler (en belirgin etiket, havuz ortalamasına göre; ≥ %125 değilse karma)");
        foreach (var (identity, (games, wins)) in s.Identities)
            Row(w, IdentityClassifier.Name(identity), $"{Pct(games, s.Matches * 2),6} takım · kazanıyor {Pct(wins, games)}{Margin(wins, games)}", "");
        w.WriteLine("  Eşleşmeler (soldaki kazanıyor, 200+ maç):");
        foreach (var ((a, b), (games, aWins)) in s.IdentityMatchups.Where(m => m.Value.Games >= 200))
            Row(w, $"  {IdentityClassifier.Name(a)} vs {IdentityClassifier.Name(b)}", $"{Pct(aWins, games)}{Margin(aWins, games)}  ({games} maç)", "");
        w.WriteLine("  hedef: üstün kimlik %55-60");
        w.WriteLine();

        w.WriteLine($"## Belirgin erken comp (Σ(erken − geç) farkı ≥ {o.EarlyCompThreshold})");
        Row(w, "Maç", $"{s.EarlyCompMatches}", "");
        Row(w, "Erken comp kazanıyor", Pct(s.EarlyCompWins, s.EarlyCompMatches) + Margin(s.EarlyCompWins, s.EarlyCompMatches), "");
        Row(w, "Erken comp kuşatmayla 35'ten önce bitiriyor", Pct(s.EarlyCompSiegeWins, s.EarlyCompMatches), "%30-45");
        Row(w, $"Scale comp 14. dk 3.000+ geride ({s.CrushedScaleMatches} maç) → kurtarıyor",
            Pct(s.CrushedScaleWins, s.CrushedScaleMatches) + Margin(s.CrushedScaleWins, s.CrushedScaleMatches), "%25-35");
        w.WriteLine();

        w.WriteLine("## Bitiş dakikası");
        foreach (var (minute, count) in s.EndMinutes)
            w.WriteLine($"  {minute,2} dk  {new string('█', (int)Math.Ceiling(count * 60.0 / s.Matches))} {Pct(count, s.Matches)}");
        w.WriteLine();

        w.WriteLine("## Şampiyonlar (kazanma oranına göre)");
        foreach (var (_, (name, games, wins)) in s.Champions.OrderByDescending(c => (double)c.Value.Wins / c.Value.Games))
            w.WriteLine($"  {name,-20} {Pct(wins, games),6}{Margin(wins, games),-8} {games,6} maç");
    }

    private static string ContextName(RuleContext context) => context switch
    {
        RuleContext.TopLane => "koridor",
        RuleContext.Gank => "gank",
        RuleContext.Skirmish => "çatışma",
        RuleContext.Objective => "objektif",
        RuleContext.Siege => "kuşatma",
        RuleContext.FinalFight => "son savaş",
        _ => context.ToString(),
    };

    private static void Row(TextWriter w, string label, string value, string target) =>
        w.WriteLine(target.Length == 0 ? $"  {label,-55} {value}" : $"  {label,-55} {value,-24} hedef {target}");

    private static string Pct(int part, int total) =>
        total == 0 ? "—" : (part * 100.0 / total).ToString("'%'0.0", Tr);

    // %95 hata payı: ±1.96 × √(p(1−p)/n). İki oran bu payların dışında farklıysa fark gürültü değildir.
    private static string Margin(int part, int total)
    {
        if (total == 0) return "";
        var p = (double)part / total;
        return (" ±" + (1.96 * Math.Sqrt(p * (1 - p) / total) * 100).ToString("0.0", Tr));
    }
}
