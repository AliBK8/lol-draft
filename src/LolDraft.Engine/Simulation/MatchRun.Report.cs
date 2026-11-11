namespace LolDraft.Engine.Simulation;

/// <summary>
/// Draft karnesi (DESIGN.md §16.11). Kural etkileri oldukları anda doğrudan etkiyle kaydedilir:
/// koridorda farm altını (kuralın katkısı çıkarılmış Δ ile karşılaştırma), savaşta kazanma ihtimali (kuralsız d ile).
/// Zar atmaz; maçın akışını değiştirmez.
/// </summary>
internal sealed partial class MatchRun
{
    // Hiper taşıyıcı potansiyelinin altın çarpanı bu eşiğin altındaysa "scale edemedi" (altını rakibinin gerisinde).
    private const double CarryStarvedFactor = 0.9;
    private const int CarryFullPowerMinute = 28;

    private sealed class RuleTally(RuleEffect effect, RuleContext context)
    {
        public RuleEffect Effect { get; } = effect;
        public RuleContext Context { get; } = context;
        public int Count { get; set; }
        public double Impact { get; set; }
    }

    // Kayıt sırasıyla liste (deterministik çıktı) + arama için sözlük (üzerinde döngü yok).
    private readonly List<RuleTally> _ruleTallies = [];
    private readonly Dictionary<(string Rule, TeamSide Side, RuleContext Context, string ChampionId, string Text), RuleTally> _ruleIndex = [];

    // Bu dakikanın koridor kuralları (ScheduleLanes yazar, farm dilimleri okur): ham Δ (K2'den önce), K2 çarpanı.
    private sealed record LaneRuleState(IReadOnlyList<RuleEffect> Effects, double RawDelta, double Factor, RuleEffect? Damping);

    private readonly LaneRuleState?[] _laneRules = new LaneRuleState?[Lanes.Length];

    private RuleTally Tally(RuleEffect effect, RuleContext context)
    {
        var key = (effect.Rule, effect.Side, context, effect.Source.Id, effect.Text);
        if (!_ruleIndex.TryGetValue(key, out var tally))
        {
            _ruleIndex[key] = tally = new RuleTally(effect, context);
            _ruleTallies.Add(tally);
        }

        return tally;
    }

    // Koridor kuralları dakika başında: etkili oldukları dakika sayılır; altın etkisi farm dilimlerinde.
    private void ReportLaneRules(Lane lane, IReadOnlyList<RuleEffect> effects, double rawDelta, double factor, RuleEffect? damping)
    {
        _laneRules[(int)lane] = new LaneRuleState(effects, rawDelta, factor, damping);
        foreach (var effect in effects)
            Tally(effect, LaneContext(lane)).Count++;
        if (damping is not null)
            Tally(damping, LaneContext(lane)).Count++;
    }

    // Bir farm diliminde her koridor kuralının doğrudan etkisi: gerçek Δ ile kuralın katkısı çıkarılmış Δ'nın
    // farm altını farkı (kuralın tarafının gözünden). K2 için karşılaştırma yumuşatılmamış Δ'yla.
    // Örnek: Δ = 0,7 (K1 +0,3 dahil), iki taraf koridorda: gerçek fark 1,5225 − 1,155 = 0,3675 CS;
    // K1'siz Δ = 0,4: 1,47 − 1,26 = 0,21 CS → K1'in bu dilimdeki etkisi 0,1575 CS × 20 = +3,15 altın.
    private void ReportLaneFarm(Lane lane, ChampionProfile blue, ChampionProfile red, double t)
    {
        if (_laneRules[(int)lane] is not { } state)
            return;

        var (bluePresent, redPresent) = (IsInLane(blue, t), IsInLane(red, t));
        var actual = state.RawDelta * state.Factor;
        foreach (var effect in state.Effects)
        {
            var without = (state.RawDelta - effect.Signed) * state.Factor;
            Tally(effect, LaneContext(lane)).Impact +=
                FarmLead(effect.Side, actual, bluePresent, redPresent) - FarmLead(effect.Side, without, bluePresent, redPresent);
        }

        if (state.Damping is { } damping)
        {
            Tally(damping, LaneContext(lane)).Impact +=
                FarmLead(damping.Side, actual, bluePresent, redPresent) - FarmLead(damping.Side, state.RawDelta, bluePresent, redPresent);
        }
    }

    private static double FarmLead(TeamSide side, double delta, bool bluePresent, bool redPresent)
    {
        var (blue, red) = SplitCs(BaseCsPerSample, delta, bluePresent, redPresent);
        return side.Sign() * (blue - red) * GoldPerCs;
    }

    // Savaşta her kuralın doğrudan etkisi: kuralın tarafının kazanma ihtimali kuralla ve kuralsız (yüzde puan).
    // Örnek: d = 4 (K5 Mavi +2 dahil), bölen 15 → %56,6; K5'siz d = 2 → %53,3 → K5 bu savaşta +3,3 puan.
    private void ReportFightRules(IReadOnlyList<RuleEffect> effects, RuleContext context, double d, double divisor)
    {
        foreach (var effect in effects)
        {
            var tally = Tally(effect, context);
            tally.Count++;
            tally.Impact += 100 * (WinChance(effect.Side, d, divisor) - WinChance(effect.Side, d - effect.Signed, divisor));
        }
    }

    private static double WinChance(TeamSide side, double d, double divisor) =>
        Probability.Clamp(Probability.Logistic(side.Sign() * d / divisor));

    // Gank'ta K7: başarı ihtimaline eklenen pay doğrudan etkidir.
    private void ReportGankRule(TeamSide side, ChampionProfile jungler, double bonus)
    {
        if (bonus == 0)
            return;

        var tally = Tally(new RuleEffect("K7", side, bonus, jungler, TeamRules.GankText(jungler)), RuleContext.Gank);
        tally.Count++;
        tally.Impact += 100 * bonus;
    }

    private DraftReport BuildReport(int minute, double t)
    {
        // Koridorda toplam altın, savaşta/gank'ta olay başına ortalama.
        var rules = _ruleTallies
            .Select(r => new RuleImpact(r.Effect.Rule, r.Effect.Side, r.Context, r.Effect.Source.Id, r.Count,
                IsLane(r.Context) ? r.Impact : r.Impact / Math.Max(1, r.Count), r.Effect.Text))
            .OrderBy(r => r.Context).ThenBy(r => r.Side).ThenBy(r => r.Rule, StringComparer.Ordinal)
            .ToList();

        var carries = new List<CarryReport>();
        foreach (var side in (TeamSide[])[TeamSide.Blue, TeamSide.Red])
        {
            foreach (var carry in Team(side).Champions.Where(c => c.IsHyperCarry))
                carries.Add(Carry(carry, side, minute, t));
        }

        return new DraftReport(rules, carries, TeamGoldReport(TeamSide.Blue, t), TeamGoldReport(TeamSide.Red, t),
            OffRoles(t));
    }

    // Kumarın sonucu: rol rakibine göre maç sonu altını. ±1.000'den büyük fark "tuttu" ya da "tutmadı".
    private const int OffRoleVerdictGold = 1000;

    // Binlik ayırıcı nokta, kültürden bağımsız (sunucunun dil ayarı metni değiştirmesin): 2400 → "2.400".
    private static readonly System.Globalization.NumberFormatInfo GoldFormat = new() { NumberGroupSeparator = "." };

    private List<OffRoleReport> OffRoles(double t)
    {
        var reports = new List<OffRoleReport>();
        foreach (var side in (TeamSide[])[TeamSide.Blue, TeamSide.Red])
        {
            var champions = Team(side).Champions;
            for (var i = 0; i < champions.Count; i++)
            {
                var champion = champions[i];
                var penalty = RolePenalty(champion);
                if (penalty == 0)
                    continue;

                var role = (ChampionRole)i;
                var diff = (int)Math.Round(_gold.GoldAt(champion, t) - _gold.GoldAt(Counterpart(champion), t));
                var where = $"{champion.Name} {RoleLocative(role)}";
                var cost = $"(−%{Math.Round(penalty * 100)})";
                var text = diff >= OffRoleVerdictGold
                    ? $"Rol dışı kumar tuttu: {where}, rakibinin {diff.ToString("#,0", GoldFormat)} altın önünde bitirdi {cost}"
                    : diff <= -OffRoleVerdictGold
                        ? $"Rol dışı kumar tutmadı: {where}, rakibinin {(-diff).ToString("#,0", GoldFormat)} altın gerisinde bitirdi {cost}"
                        : $"Rol dışı: {where}, rakibiyle başa baş bitirdi {cost}";
                reports.Add(new OffRoleReport(champion.Id, side, role, penalty, diff, text));
            }
        }

        return reports;
    }

    private static string RoleLocative(ChampionRole role) => role switch
    {
        ChampionRole.Top => "top'ta",
        ChampionRole.Jungle => "ormanda",
        ChampionRole.Mid => "mid'de",
        ChampionRole.Bot => "bot'ta",
        ChampionRole.Support => "support'ta",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    // Hiper taşıyıcı: G_eff / geç gücü. Örnek Kayle 31. dk, rakibinden %25 az altın (r 0,857, econ 3 → ×0,71):
    // 10 × 0,71 / 10 → %71, "scale edemedi".
    private CarryReport Carry(ChampionProfile carry, TeamSide side, int minute, double t)
    {
        var factor = GoldFactor(_gold.GoldAt(carry, t), _gold.GoldAt(Counterpart(carry), t), carry.Economy);
        var share = carry.PowerAt(minute) * (1 - RolePenalty(carry)) * factor / carry.Late;
        var percent = (int)Math.Round(share * 100);
        var text = factor < CarryStarvedFactor
            ? $"{carry.Name} scale edemedi (altını rakibinin gerisinde): {minute}. dk'da gücü geç potansiyelinin %{percent} kadarı"
            : minute < CarryFullPowerMinute
                ? $"{carry.Name} geç oyuna ulaşamadı: maç {minute}. dk'da bitti, gücü geç potansiyelinin %{percent} kadarı"
                : $"{carry.Name} scale etti: {minute}. dk'da gücü geç potansiyelinin %{percent} kadarı";
        return new CarryReport(carry.Id, side, minute, share, text);
    }

    // Kaynak kaynak takım altını; pasif gelir = altın − başlangıç − kazanılan.
    private TeamGoldReport TeamGoldReport(TeamSide side, double t)
    {
        var champions = Team(side).Champions;
        int Sum(GoldSource source) => (int)Math.Round(champions.Sum(c => _gold.Earned(c, source)));

        var earned = Enum.GetValues<GoldSource>().Sum(Sum);
        var total = champions.Sum(c => _gold.GoldAt(c, t) - GoldLedger.StartingGold);
        return new TeamGoldReport(side, (int)Math.Round(total) - earned, Sum(GoldSource.Farm), Sum(GoldSource.SupportItem),
            Sum(GoldSource.Kill), Sum(GoldSource.Bounty), Sum(GoldSource.Assist), Sum(GoldSource.Plate), Sum(GoldSource.Tower),
            Sum(GoldSource.Objective));
    }

    private static bool IsLane(RuleContext context) => context is RuleContext.TopLane or RuleContext.MidLane or RuleContext.BotLane;

    private static RuleContext LaneContext(Lane lane) => lane switch
    {
        Lane.Top => RuleContext.TopLane,
        Lane.Mid => RuleContext.MidLane,
        Lane.Bot => RuleContext.BotLane,
        _ => throw new ArgumentOutOfRangeException(nameof(lane)),
    };
}
