namespace LolDraft.Domain;

/// <summary>Lobinin beklenen hataları. Kodlar istemciyle sözleşmedir, değiştirilmez.</summary>
public static class LobbyErrors
{
    public static readonly Error InvalidLobbyCode = new("invalid_lobby_code", "Geçersiz lobi kodu.");
    public static readonly Error InvalidPlayerId = new("invalid_player_id", "Geçersiz oyuncu kimliği.");
    public static readonly Error NotFound = new("lobby_not_found", "Lobi bulunamadı.");
    public static readonly Error Full = new("lobby_full", "Lobi dolu.");
    public static readonly Error AlreadyInLobby = new("already_in_lobby", "Oyuncu zaten bu lobide.");
    public static readonly Error NotInLobby = new("not_in_lobby", "Oyuncu bu lobide değil.");
    public static readonly Error NotFull = new("lobby_not_full", "Rakip henüz katılmadı.");
    public static readonly Error InvalidActionId = new("invalid_action_id", "Geçersiz hamle kimliği.");
}
