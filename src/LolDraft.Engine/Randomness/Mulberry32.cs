namespace LolDraft.Engine.Randomness;

/// <summary>
/// mulberry32 (DESIGN.md §4.1). Prototipteki JS fonksiyonuyla bit bit aynı sayıları üretir.
/// Durum tek bir 32 bitlik sayı; her çağrıda sabit bir adım eklenir, sonra bitler karıştırılır.
/// </summary>
internal sealed class Mulberry32 : IRandomSource
{
    private uint _state;

    public Mulberry32(Seed seed) => _state = seed.Value;

    public double NextDouble()
    {
        // JS karşılıkları: Math.imul → uint çarpma (taşma kırpılır), x >>> n → uint üzerinde x >> n, |0 → uint taşması.
        unchecked
        {
            // a = a + 0x6D2B79F5 | 0
            // Sabit adım (Weyl dizisi): tek sayı olduğu için 2^32 durumun hepsini tekrarsız gezer.
            _state += 0x6D2B79F5;

            // Karıştırma: kaydır-XOR ve çarpmalar, durumun her bitini sonucun her bitine yayar.
            // t = Math.imul(a ^ a >>> 15, 1 | a)
            uint t = (_state ^ (_state >> 15)) * (_state | 1);
            // t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t      (JS'te + önce, ^ sonra uygulanır)
            t = (t + ((t ^ (t >> 7)) * (t | 61))) ^ t;

            // ((t ^ t >>> 14) >>> 0) / 2^32  →  [0, 1)
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public double NextNormal()
    {
        // Box-Muller: iki düzgün sayıdan (u, v) bir normal sayı üretir.
        //   z = √(−2·ln u) · cos(2π·v)
        // √(−2·ln u) merkezden uzaklığı, 2π·v açıyı verir; bu noktanın x izdüşümü N(0, 1) dağılır.
        // ln 0 tanımsız olduğu için 0 gelirse yeniden çekilir (JS: while(!u) u = rng()).
        double u, v;
        do u = NextDouble(); while (u == 0);
        do v = NextDouble(); while (v == 0);
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }
}
