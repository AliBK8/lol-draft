using LolDraft.Engine.Events;
using LolDraft.Engine.Simulation;

namespace LolDraft.Engine.Tests;

/// <summary>Yumuşak güç eğrisi ve altından gerçek güç (DESIGN.md §16.3, §16.5).</summary>
public sealed class PowerTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(10, 2)]
    [InlineData(14, 4)]   // erken → orta yolun yarısı
    [InlineData(18, 6)]
    [InlineData(23, 8)]   // orta → geç yolun yarısı
    [InlineData(28, 10)]
    [InlineData(35, 10)]
    public void Kayle_gucu_yavas_yavas_artar(int minute, double expected)
    {
        Assert.Equal(expected, TestTeams.ScaleRed().Top.PowerAt(minute), 9);
    }

    [Theory]
    [InlineData(5000, 5000, 1.0)]     // başa baş
    [InlineData(5500, 4500, 1.15)]    // r = 1,1 → 1 + 1,5 × 0,1
    [InlineData(4500, 5500, 0.85)]
    [InlineData(10000, 1000, 1.4)]    // tavan
    [InlineData(1000, 10000, 0.5)]    // taban
    public void Altin_carpani_rakiple_kiyaslanir_ve_sinirlidir(double gold, double counterpart, double expected)
    {
        Assert.Equal(expected, MatchRun.GoldFactor(gold, counterpart, economy: 2), 9);
    }

    [Fact]
    public void Support_rakip_supportla_kiyaslanir_rolu_yuzunden_geride_sayilmaz()
    {
        // Support 3.000, karşı support 3.000: koridorcuların 5.000'i önemli değil.
        Assert.Equal(1.0, MatchRun.GoldFactor(3000, 3000, economy: 0), 9);
    }

    // s = 0,5 + 0,5 × econ. r = 1,1 (5.500 vs 4.500) ve r = 0,9.
    [Theory]
    [InlineData(0, 1.05, 0.95)]
    [InlineData(1, 1.10, 0.90)]
    [InlineData(2, 1.15, 0.85)]
    [InlineData(3, 1.20, 0.80)]
    public void Altina_bagimli_sampiyon_altindan_daha_cok_etkilenir(int economy, double ahead, double behind)
    {
        Assert.Equal(ahead, MatchRun.GoldFactor(5500, 4500, economy), 9);
        Assert.Equal(behind, MatchRun.GoldFactor(4500, 5500, economy), 9);
    }

    [Fact]
    public void Hiper_tasiyici_nisanci_ya_da_buyucu_econ_3_ve_gec_gucu_yuksek()
    {
        var blue = TestTeams.EarlyBlue();
        var red = TestTeams.ScaleRed();

        Assert.True(red.Top.IsHyperCarry);      // Kayle: nişancı/büyücü, econ 3, l 10
        Assert.True(red.Mid.IsHyperCarry);      // Kassadin: suikastçı/büyücü (ikincil sınıf da sayılır)
        Assert.True(red.Bot.IsHyperCarry);      // Jinx
        Assert.False(blue.Bot.IsHyperCarry);    // Draven: econ 2, l 5
        Assert.False(red.Jungle.IsHyperCarry);  // Sejuani: tank
    }

    // Eşit draft'ta fark sadece altından gelir: objektif savaşında, dakika başında 1.500+ altın önde olan daha çok kazanır.
    [Fact]
    public void Esit_draftta_altinda_onde_olan_savasi_daha_cok_kazanir()
    {
        var simulator = MatchEngine.CreateSimulator();
        var (wins, fights) = (0, 0);
        for (var seed = 0u; seed < 1000; seed++)
        {
            var result = simulator.Simulate(TestTeams.Even(seed));
            foreach (var fight in result.Events.OfType<ObjectiveEvent>())
            {
                var frame = result.GoldFrames.First(f => f.Minute == (int)(fight.T / 60));
                var lead = frame.TeamGold(TeamSide.Blue) - frame.TeamGold(TeamSide.Red);
                if (Math.Abs(lead) < 1500)
                    continue;
                fights++;
                if (fight.Side == (lead > 0 ? TeamSide.Blue : TeamSide.Red))
                    wins++;
            }
        }

        // Ölçüm: s = 1,5 ile %59,5 (s = 0,9 ile %55,7); altın etkisi kapatılınca %50,6. Sınır aradaki boşlukta.
        // Etkinin büyüklüğü faz D'de veriyle ayarlanacak (s, taban/tavan).
        Assert.True(fights > 500, $"Sadece {fights} savaş");
        Assert.InRange(wins / (double)fights, 0.53, 0.9);
    }

    // CS anlatımındaki sayılar gerçek CS'dir: olayın dakikasının başı ve sonu arasındadır, fark ≥ 15.
    [Fact]
    public void Cs_anlatimi_gercek_cs_farkini_soyler()
    {
        var simulator = MatchEngine.CreateSimulator();
        var count = 0;
        for (var seed = 0u; seed < 300; seed++)
        {
            foreach (var input in (MatchInput[])[TestTeams.Match(seed), TestTeams.Stomp(seed)])
            {
                var result = simulator.Simulate(input);
                foreach (var cs in result.Events.OfType<CsEvent>())
                {
                    count++;
                    var minute = (int)(cs.T / 60);
                    var numbers = cs.Text[(cs.Text.LastIndexOf('(') + 1)..^1].Split(" - ").Select(int.Parse).ToArray();
                    var (leadCs, rivalCs) = (numbers[0], numbers[1]);
                    var lead = Face(cs.Side == TeamSide.Blue ? input.Blue : input.Red, cs.Lane);
                    var rival = Face(cs.Side == TeamSide.Blue ? input.Red : input.Blue, cs.Lane);

                    Assert.True(leadCs - rivalCs >= 15, cs.Text);
                    Assert.InRange(leadCs, Cs(result, minute, lead), Cs(result, minute + 1, lead));
                    Assert.InRange(rivalCs, Cs(result, minute, rival), Cs(result, minute + 1, rival));
                }
            }
        }

        Assert.True(count > 100, $"Sadece {count} CS anlatımı");
    }

    private static ChampionProfile Face(TeamDraft team, Lane lane) => TestEvents.LaneChampions(team, lane)[0];

    private static int Cs(MatchResult result, int minute, ChampionProfile champion) =>
        result.GoldFrames.First(f => f.Minute == Math.Min(minute, result.End.Minute))
            .Champions.Single(c => c.ChampionId == champion.Id).Cs;
}
