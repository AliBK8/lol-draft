using Microsoft.Data.Sqlite;

namespace LolDraft.Infrastructure.Matches;

/// <summary>
/// Maç veritabanının yeri (DESIGN.md §8.16): üretimde dosya (Docker volume'u), geliştirme ve testte istenirse bellek.
/// Bellektekinin adı her örnekte farklı: aynı süreçteki iki uygulama (ör. iki test sunucusu) birbirinin maçını görmez.
/// </summary>
public sealed class MatchStorage
{
    private MatchStorage(string connectionString, string? filePath)
    {
        ConnectionString = connectionString;
        FilePath = filePath;
    }

    public string ConnectionString { get; }

    /// <summary>Dosyadaysa yolu; bellekteyse null.</summary>
    public string? FilePath { get; }

    public bool InMemory => FilePath is null;

    public static MatchStorage File(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Builder: yol içindeki ';' ya da '=' bağlantı dizisini bozamaz.
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate };
        return new MatchStorage(connection.ToString(), path);
    }

    public static MatchStorage Memory()
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = $"matches-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        return new MatchStorage(connection.ToString(), null);
    }
}
