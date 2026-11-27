namespace LolDraft.Engine.Events;

/// <summary>Maçta olan bir şey (DESIGN.md §4.9). <paramref name="T"/> oyun saniyesi; boş <paramref name="Text"/> log'a yazılmaz.</summary>
public abstract record MatchEvent(double T, string Text);

public enum Objective
{
    Dragon,
    Herald,
    Baron,
    Elder,
    FinalFight,
}

/// <param name="Probability">Kazananın bu savaşı kazanma ihtimali (0.1-0.9).</param>
public sealed record ObjectiveEvent(double T, string Text, Objective Objective, TeamSide Side, double Probability)
    : MatchEvent(T, Text);

/// <param name="Side">Kill'i alan takım.</param>
/// <param name="Until">Kurbanın yeniden doğduğu oyun saniyesi.</param>
/// <param name="Lane">Koridor olayıysa koridor; savaş kill'lerinde boş.</param>
/// <param name="Bounty">Katilin kill altınına ek aldığı shutdown ödülü (0-700, DESIGN.md §16.4).</param>
public sealed record KillEvent(
    double T, string Text, TeamSide Side, string KillerId, string VictimId, double Until, Lane? Lane, int Bounty)
    : MatchEvent(T, Text);

/// <param name="Tier">0 dış kule, 1 iç kule, 2 inhibitör.</param>
public sealed record TowerEvent(double T, string Text, TeamSide Side, Lane Lane, int Tier) : MatchEvent(T, Text);

public sealed record PlateEvent(double T, string Text, TeamSide Side, Lane Lane) : MatchEvent(T, Text);

public sealed record CsEvent(double T, string Text, TeamSide Side, Lane Lane) : MatchEvent(T, Text);

/// <summary>
/// Sürpriz işareti (metinsiz, haritada rozet). Etkisi aynı saniyedeki <see cref="KillEvent"/>: sürprizi yapan taraf
/// koridorda kill alır (outplay ya da geri püskürtülen dive); log satırı o kill'dir.
/// </summary>
/// <param name="Value">Döküm puanı (3-5); karara girmez.</param>
public sealed record SurpriseEvent(double T, string Text, TeamSide Side, Lane Lane, double Value) : MatchEvent(T, Text);

/// <param name="Side">Gank'ı atan takım.</param>
public sealed record GankFailEvent(double T, string Text, TeamSide Side, Lane Lane) : MatchEvent(T, Text);

/// <summary>
/// Görünmez hareket ipucu: <paramref name="ParticipantIds"/> olaydan <paramref name="Lead"/> sn önce hedefe yürür,
/// <paramref name="Tail"/> sn kalır.
/// </summary>
/// <param name="Lane">Gank hedefi koridor; nehir çatışmasında boş.</param>
/// <param name="River">Nehir çatışmasının yeri; gank'ta boş.</param>
public sealed record MoveEvent(
    double T, string Text, IReadOnlyList<string> ParticipantIds, Lane? Lane, RiverSide? River, int Lead, int Tail)
    : MatchEvent(T, Text)
{
    // Record eşitliği listeyi referansla karşılaştırır; aynı içerikli iki olay eşit sayılsın diye içerikle karşılaştır.
    public bool Equals(MoveEvent? other) =>
        other is not null
        && base.Equals(other)
        && ParticipantIds.SequenceEqual(other.ParticipantIds)
        && Lane == other.Lane && River == other.River && Lead == other.Lead && Tail == other.Tail;

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), ParticipantIds.Count, Lane, River, Lead, Tail);
}

/// <param name="Side">Çatışmayı kazanan takım.</param>
/// <param name="Probability">Kazananın bu çatışmayı kazanma ihtimali (0.1-0.9).</param>
public sealed record SkirmishEvent(
    double T, string Text, TeamSide Side, double Probability, RiverSide River, int BlueCount, int RedCount)
    : MatchEvent(T, Text);

/// <summary>
/// Ormancı istilası (DESIGN.md §4.5 Orman): <paramref name="Side"/> rakibin ormanındaki <paramref name="Camp"/>'a girer.
/// Cevap yoksa kampı çalar; cevap varsa çatışma olur (kill'ler ayrı <see cref="KillEvent"/>). Katılanlar olaydan
/// <paramref name="Lead"/> sn önce kampa yürür, <paramref name="Tail"/> sn kalır.
/// </summary>
/// <param name="Side">İstila eden takım.</param>
/// <param name="DefenderIds">Cevap veren rakipler; boşsa kimse gelmedi.</param>
/// <param name="Stolen">İstilacılar kampı aldı mı (cevapsız ya da çatışmayı kazandı).</param>
/// <param name="Probability">Çatışma olduysa istilacıların kazanma ihtimali (0.1-0.9); cevapsızsa boş.</param>
public sealed record InvadeEvent(
    double T, string Text, TeamSide Side, JungleCamp Camp, IReadOnlyList<string> InvaderIds,
    IReadOnlyList<string> DefenderIds, bool Stolen, double? Probability, int Lead, int Tail)
    : MatchEvent(T, Text)
{
    // Record eşitliği listeleri referansla karşılaştırır; içerikle karşılaştır (MoveEvent gibi).
    public bool Equals(InvadeEvent? other) =>
        other is not null
        && base.Equals(other)
        && Side == other.Side && Camp == other.Camp
        && InvaderIds.SequenceEqual(other.InvaderIds) && DefenderIds.SequenceEqual(other.DefenderIds)
        && Stolen == other.Stolen && Probability == other.Probability && Lead == other.Lead && Tail == other.Tail;

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), Side, Camp, InvaderIds.Count, DefenderIds.Count, Stolen, Lead, Tail);
}

/// <summary>Görünmez hareket ipucu: takım <paramref name="Lead"/> sn önce toplanır, <paramref name="Tail"/> sn kalır.</summary>
/// <param name="Lane">Kuşatmanın girdiği koridor (DESIGN.md §4.11); son savaştan sonra Nexus'a yürüyüşte boş.</param>
public sealed record SiegeEvent(double T, string Text, TeamSide Side, int Lead, int Tail, Lane? Lane = null) : MatchEvent(T, Text);

/// <summary>
/// Görünmez: <paramref name="Side"/> takımının yeni harita düzeni (DESIGN.md §4.11). Ekran rolü koridora bununla çevirir.
/// </summary>
/// <param name="Posts">Takım sırasıyla (top, jungle, mid, bot, support) koridor; boşsa ormanında (ormancı, Grup hariç).</param>
public sealed record LayoutEvent(double T, string Text, TeamSide Side, MapLayout Layout, IReadOnlyList<Lane?> Posts)
    : MatchEvent(T, Text)
{
    // Record eşitliği listeyi referansla karşılaştırır; içerikle karşılaştır (MoveEvent gibi).
    public bool Equals(LayoutEvent? other) =>
        other is not null && base.Equals(other) && Side == other.Side && Layout == other.Layout && Posts.SequenceEqual(other.Posts);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Side, Layout, Posts.Count);
}

/// <summary>
/// Split (DESIGN.md §16.8 K12): objektif savaşı sırasında <paramref name="SplitterId"/> yan koridorda baskı kurar.
/// Cevap veren rakip (<paramref name="AnswererId"/>) varsa ikisi düello yapar; yoksa splitter kuleyi karşılıksız alır.
/// </summary>
public sealed record SplitEvent(double T, string Text, TeamSide Side, Lane Lane, string SplitterId, string? AnswererId)
    : MatchEvent(T, Text);

/// <summary>
/// Yakalanma (DESIGN.md §4.11 D): <paramref name="Side"/> takımının avcıları <paramref name="VictimId"/>'yi bir yerde
/// yakalar: koridorda (<paramref name="Lane"/>) ya da bir takımın ormanında (<paramref name="Jungle"/>, kampı
/// <paramref name="Camp"/>). Kurbanın yakındaki takım arkadaşları yardıma gelirse (<paramref name="HelperIds"/>) küçük bir
/// savaş olur. Kill'ler ayrı <see cref="KillEvent"/>. Katılanlar olaydan <paramref name="Lead"/> sn önce yere yürür.
/// </summary>
/// <param name="Text">Savaş olduysa log satırı; düz yakalanmada boş (log satırı kill'in kendisi).</param>
/// <param name="Probability">Savaş olduysa avcıların kazanma ihtimali; yoksa boş.</param>
public sealed record CatchEvent(
    double T, string Text, TeamSide Side, string VictimId, IReadOnlyList<string> HunterIds, IReadOnlyList<string> HelperIds,
    Lane? Lane, TeamSide? Jungle, JungleCamp? Camp, double? Probability, int Lead, int Tail)
    : MatchEvent(T, Text)
{
    // Record eşitliği listeleri referansla karşılaştırır; içerikle karşılaştır (MoveEvent gibi).
    public bool Equals(CatchEvent? other) =>
        other is not null && base.Equals(other) && Side == other.Side && VictimId == other.VictimId
        && HunterIds.SequenceEqual(other.HunterIds) && HelperIds.SequenceEqual(other.HelperIds)
        && Lane == other.Lane && Jungle == other.Jungle && Camp == other.Camp && Probability == other.Probability
        && Lead == other.Lead && Tail == other.Tail;

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Side, VictimId, HunterIds.Count, Lane, Jungle, Camp);
}

/// <summary>
/// Görünmez (DESIGN.md §4.11 D): objektif savaşına çukurun öbür ucundaki yan koridordan gelenler; savaşa geç ve rakibin
/// arkasından katılırlar.
/// </summary>
public sealed record FlankEvent(double T, string Text, TeamSide Side, Objective Objective, IReadOnlyList<string> FlankerIds)
    : MatchEvent(T, Text)
{
    public bool Equals(FlankEvent? other) =>
        other is not null && base.Equals(other) && Side == other.Side && Objective == other.Objective
        && FlankerIds.SequenceEqual(other.FlankerIds);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Side, Objective, FlankerIds.Count);
}

/// <summary>4. ejderi alan takım ejder ruhunu kazanır; ardından Elder doğar.</summary>
public sealed record DragonSoulEvent(double T, string Text, TeamSide Side) : MatchEvent(T, Text);

/// <param name="Side">Kuşatmayı tutan (savunan) takım.</param>
/// <param name="Probability">Savunanın kuşatmayı tutma ihtimali.</param>
public sealed record ThrowEvent(double T, string Text, TeamSide Side, double Probability) : MatchEvent(T, Text);

/// <summary>Maçın son olayı.</summary>
/// <param name="Probability">Kuşatmayla bittiyse kuşatmanın başarı ihtimali; son savaşla bittiyse boş.</param>
public sealed record NexusEvent(double T, string Text, TeamSide Side, double? Probability = null) : MatchEvent(T, Text);
