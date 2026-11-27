namespace LolDraft.Engine;

/// <summary>Takımın harita düzeni (DESIGN.md §4.11): 14. dakikadan sonra kim hangi koridorda.</summary>
public enum MapLayout
{
    /// <summary>Standart: top üst, mid orta, adc + support alt.</summary>
    Lanes,

    /// <summary>Beş kişi tek koridorda (ormancı dahil).</summary>
    Group,

    /// <summary>Bir splitçi yan koridorda, dört kişi mid'de.</summary>
    OneFour,

    /// <summary>İki yan koridor, üç kişi mid'de.</summary>
    OneThreeOne,
}
