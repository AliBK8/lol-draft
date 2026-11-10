namespace LolDraft.Domain;

/// <summary>Serinin beklenen hataları. Kodlar istemciyle sözleşmedir, değiştirilmez.</summary>
public static class SeriesErrors
{
    public static readonly Error SeriesFinished = new("series_finished", "Seri bitti.");
    public static readonly Error GameInProgress = new("game_in_progress", "Devam eden maç bitmeden yenisi başlatılamaz.");
    public static readonly Error DraftNotCompleted = new("draft_not_completed", "Draft bitmeden maç sonucu yazılamaz.");
}
