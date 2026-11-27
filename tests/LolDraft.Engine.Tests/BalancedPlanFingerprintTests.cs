using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LolDraft.Engine.Tests;

/// <summary>
/// Oyun planı (DESIGN.md §4.10) "Dengeli" düğmelere dokunmaz: plansız (Dengeli × Dengeli) maç, plan kodu eklenmeden
/// önceki motorla bit bit aynı. Parmak izi plan kodundan önce alındı (2026-11-24, b49a6ac). Bu test kırılırsa ya Dengeli
/// bir şeyi değiştirdi (hata) ya da motor bilerek değişti (o zaman parmak izi yenilenir ve denge yeniden ölçülür).
/// Yenilemeler: 2026-11-27 alt sistem başına zar akışları (DESIGN.md §4.1; davranış aynı, zarlar başka akıştan);
/// 2026-11-27 harita düzeni (§4.11, motor bilerek değişti); 2026-11-27 kule baskısı (§4.11 C); 2026-11-27 konumlu yakalanma + arkadan kapatma (§4.11 D).
/// </summary>
public sealed class BalancedPlanFingerprintTests
{
    private const int MatchCount = 100;

    // Match, Even, Stomp, Split × 100 seed: olaylar (tip, saniye, metin), sonuç ve dakika kareleri.
    private const string Expected = "F987A24936DCC8A29A48DFB0963199FF00A21FA9E0097E1C319BDCE3FA262FAD";

    [Fact]
    public void Dengeli_x_Dengeli_plansiz_motorla_bit_bit_ayni()
    {
        var actual = Fingerprint();
        Assert.True(Expected == actual, $"parmak izi: {actual}");
    }

    internal static string Fingerprint()
    {
        var simulator = MatchEngine.CreateSimulator();
        var text = new StringBuilder();
        for (uint seed = 0; seed < MatchCount; seed++)
        {
            foreach (var input in (MatchInput[])[TestTeams.Match(seed), TestTeams.Even(seed), TestTeams.Stomp(seed), TestTeams.Split(seed)])
            {
                var result = simulator.Simulate(input);
                foreach (var e in result.Events)
                    text.Append(e.GetType().Name).Append('|').Append(e.T.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(e.Text).Append('\n');
                var end = result.End;
                text.Append(end.Minute).Append(end.Winner).Append(end.Kind)
                    .Append(end.WinnerProbability.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                foreach (var frame in result.GoldFrames)
                {
                    foreach (var c in frame.Champions)
                        text.Append(c.Gold).Append(',').Append(c.Cs).Append(';');
                    text.Append('\n');
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
