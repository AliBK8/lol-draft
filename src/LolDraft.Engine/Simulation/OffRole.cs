namespace LolDraft.Engine.Simulation;

/// <summary>
/// Rol dışı oynama (DESIGN.md §16.8, karar 2026-11-12): ceza değil kumar.
/// <list type="bullet">
/// <item>Ortalama ceza: güç eğrisi (G_pot) rol mesafesi kadar küçülür. Yakın rol (top↔mid) az, uzak rol (support↔carry) çok.</item>
/// <item>Oynaklık: lane fazında (dk &lt; 14) rol dışı oyuncunun olduğu koridorun gürültüsü × 1,5. Ortalamayı değiştirmez,
/// sonucu iki yöne açar: koridor kartopuyla rol dışı ya ezer ya ezilir. Hikâye üretir ("Leona ADC... ve tuttu").</item>
/// </list>
/// Ortalama bonus bilerek yok: olsaydı rol dışı counterpick optimal strateji olurdu. Eşleşme avantajı zaten kurallardan
/// gelir (ör. K1: ranged büyücü top'ta melee'ye karşı +0,3/dk); ceza onun bedelidir.
/// Bilinen flex'ler (Jayce top) champions.json'da roles'a yazılır, cezasız ve sakin oynar.
/// </summary>
internal static class OffRole
{
    // Ceza [doğal rol, oynanan yuva]; simetrik. Başlangıç değerleri, faz D'de denge aracıyla ayarlanır.
    //   %20 yakın: aynı tip iş (top↔mid fighter/büyücü, mid↔bot ranged hasar)
    //   %25 orman: kamp ve gank ritmi ayrı beceri
    //   %30 büyücü/support geçişi (Lux, Brand örnekleri var ama nadir)
    //   %35 uzak: support ↔ carry roller, bot ↔ top/orman
    private static readonly double[,] Penalties =
    {
        //          top    jg     mid    bot    sup
        /* top */ { 0.00,  0.25,  0.20,  0.35,  0.35 },
        /* jg  */ { 0.25,  0.00,  0.25,  0.35,  0.35 },
        /* mid */ { 0.20,  0.25,  0.00,  0.20,  0.30 },
        /* bot */ { 0.35,  0.35,  0.20,  0.00,  0.30 },
        /* sup */ { 0.35,  0.35,  0.30,  0.30,  0.00 },
    };

    /// <summary>Rol dışı koridorun (ve ormanın) lane fazındaki gürültü çarpanı: σ 0,8 → 1,2.</summary>
    public const double NoiseFactor = 1.5;

    /// <summary>Oynaklık bu dakikaya kadar (lane fazı; rolün işini bilmek en çok koridorda fark eder).</summary>
    public const int VolatileUntilMinute = 14;

    /// <summary>
    /// Yuvadaki şampiyonun cezası (0 = kendi rolünde). Çok rollü şampiyon en yakın rolünü sayar:
    /// mid + sup büyücü bot'ta → min(mid→bot %20, sup→bot %30) = %20. Rol bilgisi yoksa 0.
    /// </summary>
    public static double Penalty(ChampionProfile champion, ChampionRole played)
    {
        var roles = champion.Roles;
        return roles.Count == 0 ? 0 : roles.Min(role => Penalties[(int)role, (int)played]);
    }
}
