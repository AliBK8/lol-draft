namespace LolDraft.Engine.Randomness;

/// <summary>Motordaki tek rastgelelik kaynağı. Saat, Random.Shared, Guid gibi kaynaklar motorda yasak.</summary>
internal interface IRandomSource
{
    /// <summary>[0, 1) aralığında düzgün dağılımlı sayı.</summary>
    double NextDouble();

    /// <summary>Standart normal dağılım N(0, 1).</summary>
    double NextNormal();
}
