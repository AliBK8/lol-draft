using System.Text.Json;
using LolDraft.Application.Matches;

namespace LolDraft.Api.Matches;

/// <summary>
/// MatchReady'nin gövdesi: maçın dondurulmuş belgesi (DESIGN.md §8.16). SignalR'ın JSON protokolü JsonElement'i
/// olduğu gibi yazar; canlı yayın, diskteki kayıt ve GET /api/matches/{id} aynı belgedir (tek çeviri, Application'da).
/// </summary>
internal static class ReplayPayload
{
    public static JsonElement Of(MatchRecord match) => JsonSerializer.Deserialize<JsonElement>(match.Replay.Json.Span);
}
