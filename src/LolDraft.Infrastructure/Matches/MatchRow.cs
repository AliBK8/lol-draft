using System.IO.Compression;
using LolDraft.Application.Matches;
using LolDraft.Domain;
using LolDraft.Engine;

namespace LolDraft.Infrastructure.Matches;

/// <summary>
/// `Matches` tablosunun bir satırı (DESIGN.md §8.16). Domain tipleri burada ilkel sütunlara çevrilir; EF sadece bu
/// sınıfı bilir. Belge Brotli ile sıkıştırılır: ~32 KB JSON tekrarlı alan adlarıyla dolu, ~5 KB'a iner (6 kat; 20 maç ölçümü).
/// </summary>
internal sealed class MatchRow
{
    // EF kurucu bağlaması: parametre adları özelliklerle aynı.
    public MatchRow(
        string id, string lobby, int gameNumber, string blueTeam, long seed, string engineVersion, long playedAt,
        int replayVersion, byte[] replay)
    {
        Id = id;
        Lobby = lobby;
        GameNumber = gameNumber;
        BlueTeam = blueTeam;
        Seed = seed;
        EngineVersion = engineVersion;
        PlayedAt = playedAt;
        ReplayVersion = replayVersion;
        Replay = replay;
    }

    public string Id { get; private set; }

    /// <summary>Teşhis için (hangi lobide, hangi seride oynandı); sorguda kullanılmaz.</summary>
    public string Lobby { get; private set; }

    public int GameNumber { get; private set; }

    public string BlueTeam { get; private set; }

    /// <summary>32 bit seed (uint, SQLite'ta INTEGER). Teşhis içindir; istemciye gitmez.</summary>
    public long Seed { get; private set; }

    public string EngineVersion { get; private set; }

    /// <summary>Unix milisaniye (UTC). Tamsayı: SQLite tarih tipi bilmez, karşılaştırma ve indeks sayıyla doğru çalışır.</summary>
    public long PlayedAt { get; private set; }

    public int ReplayVersion { get; private set; }

    /// <summary>Brotli ile sıkıştırılmış UTF-8 JSON.</summary>
    public byte[] Replay { get; private set; }

    public static MatchRow Of(MatchRecord match) => new(
        match.Id.Value,
        match.Lobby.Value,
        match.GameNumber,
        match.BlueTeam.ToString(),
        match.Seed.Value,
        match.EngineVersion,
        match.PlayedAt.Value.ToUnixTimeMilliseconds(),
        match.Replay.Version,
        Compress(match.Replay.Json.Span));

    /// <summary>
    /// Satır → kayıt. Sütunları sadece bu sınıf yazdığı için bozuk değer programcı ya da elle müdahale hatasıdır:
    /// sessizce yutulmaz, fırlatır.
    /// </summary>
    public MatchRecord ToRecord() => new(
        MatchId.Create(Id).Value,
        LobbyCode.Create(Lobby).Value,
        GameNumber,
        Enum.Parse<TeamSlot>(BlueTeam),
        new Seed(checked((uint)Seed)),
        EngineVersion,
        new Instant(DateTimeOffset.FromUnixTimeMilliseconds(PlayedAt)),
        new Replay(ReplayVersion, Decompress(Replay)));

    private static byte[] Compress(ReadOnlySpan<byte> json)
    {
        using var output = new MemoryStream();
        // Optimal: sıkıştırma maç başına bir kez ve ~ms; okuma (açma) seviyeden bağımsız hızlı.
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
            brotli.Write(json);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var input = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
