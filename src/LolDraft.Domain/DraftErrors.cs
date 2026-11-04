namespace LolDraft.Domain;

/// <summary>Draft'ın beklenen hataları. Kodlar istemciyle sözleşmedir, değiştirilmez.</summary>
public static class DraftErrors
{
    public static readonly Error InvalidChampionId = new("invalid_champion_id", "Geçersiz şampiyon kimliği.");
    public static readonly Error PoolTooSmall = new("pool_too_small", "Şampiyon havuzu bu draft için yetersiz.");
    public static readonly Error PhaseClosed = new("phase_closed", "Bu işlem şu anki fazda yapılamaz.");
    public static readonly Error TurnExpired = new("turn_expired", "Süre doldu.");
    public static readonly Error NotExpired = new("not_expired", "Süre henüz dolmadı.");
    public static readonly Error NotYourTurn = new("not_your_turn", "Sıra sende değil.");
    public static readonly Error WrongAction = new("wrong_action", "Bu adımda bu hamle yapılamaz (ban/pick).");
    public static readonly Error NotInPool = new("not_in_pool", "Şampiyon bu lobinin havuzunda yok.");
    public static readonly Error ChampionUnavailable = new("champion_unavailable", "Şampiyon ban'lı ya da seçilmiş.");
    public static readonly Error LockedByFearless = new("locked_by_fearless", "Şampiyon fearless kuralıyla kilitli.");
    public static readonly Error InvalidRole = new("invalid_role", "Geçersiz yuva.");
    public static readonly Error SameRole = new("same_role", "Aynı yuva kendisiyle değiştirilemez.");
    public static readonly Error LineupLocked = new("lineup_locked", "Dizilim kilitlendi, artık değiştirilemez.");
}
