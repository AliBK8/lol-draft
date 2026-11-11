namespace LolDraft.Engine;

/// <summary>
/// Draft karnesi (DESIGN.md §16.11): maçta etkili olan kurallar, hiper taşıyıcıların ne kadar scale ettiği ve
/// altının nereden geldiği. "Draft yönü belirler, oyun o yönün gerçekleşip gerçekleşmediğini" zincirinin açıklaması.
/// </summary>
public sealed record DraftReport(
    IReadOnlyList<RuleImpact> Rules, IReadOnlyList<CarryReport> Carries, TeamGoldReport Blue, TeamGoldReport Red,
    IReadOnlyList<OffRoleReport> OffRoles)
{
    // Record eşitliği listeyi referansla karşılaştırır; aynı içerikli iki karne eşit sayılsın diye içerikle karşılaştır.
    public bool Equals(DraftReport? other) =>
        other is not null && Rules.SequenceEqual(other.Rules) && Carries.SequenceEqual(other.Carries)
        && Blue == other.Blue && Red == other.Red && OffRoles.SequenceEqual(other.OffRoles);

    public override int GetHashCode() => HashCode.Combine(Rules.Count, Carries.Count, Blue, Red, OffRoles.Count);
}

/// <summary>
/// Rol dışı oynayan şampiyon ve kumarın sonucu (DESIGN.md §16.8): maç sonunda rol rakibine göre altın farkı.
/// </summary>
/// <param name="Penalty">Güç cezası (0,2 = −%20).</param>
/// <param name="GoldDiff">Maç sonunda aynı yuvadaki rakibe göre altın farkı (pozitif = önde).</param>
/// <param name="Text">"Rol dışı kumar tuttu: Syndra top'ta, rakibinin 2.400 altın önünde bitirdi (−%20)".</param>
public sealed record OffRoleReport(
    string ChampionId, TeamSide Side, ChampionRole Role, double Penalty, int GoldDiff, string Text);

/// <summary>Kuralın etkili olduğu yer.</summary>
public enum RuleContext
{
    TopLane,
    MidLane,
    BotLane,
    Gank,
    Skirmish,
    Objective,
    Siege,
    FinalFight,
}

/// <summary>
/// Bir kuralın maç boyunca etkisi, <paramref name="Side"/>'ın gözünden (DESIGN.md §16.8).
/// Doğrudan etki: kuralın katkısı çıkarılınca o an ne değişirdi; sonraki kartopu sayılmaz.
/// </summary>
/// <param name="Rule">K1-K13.</param>
/// <param name="Count">Koridorda etkili olduğu dakika; savaşta/gank'ta etkili olduğu olay sayısı.</param>
/// <param name="Impact">
/// Koridorda: farm altını farkına toplam katkısı (altın). Savaşta ve gank'ta: kazanma/başarı ihtimaline ortalama katkısı
/// (yüzde puan). Negatif = kural bu tarafın aleyhine (ör. frontline eksik).
/// </param>
/// <param name="Text">"Range üstünlüğü (Jayce)".</param>
public sealed record RuleImpact(
    string Rule, TeamSide Side, RuleContext Context, string ChampionId, int Count, double Impact, string Text);

/// <summary>Hiper taşıyıcı maç sonunda geç potansiyelinin ne kadarına ulaştı (güç eğrisi × altın).</summary>
/// <param name="PotentialShare">G_eff / geç gücü: 1 = tam potansiyel; altınla 1,4'e çıkabilir.</param>
public sealed record CarryReport(string ChampionId, TeamSide Side, int Minute, double PotentialShare, string Text);

/// <summary>Takımın maç sonundaki altınının kaynakları (başlangıç 500 × 5 hariç).</summary>
/// <param name="Passive">Yaşarken pasif gelir.</param>
public sealed record TeamGoldReport(
    TeamSide Side, int Passive, int Farm, int SupportItem, int Kill, int Bounty, int Assist, int Plate, int Tower, int Objective)
{
    public int Total => Passive + Farm + SupportItem + Kill + Bounty + Assist + Plate + Tower + Objective;
}
