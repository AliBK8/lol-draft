using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LolDraft.Infrastructure.Matches;

/// <summary>
/// Oynanmış maçların veritabanı (DESIGN.md §8.16): tek tablo, `Matches`. Şema migration'larla değişir
/// (`Matches/Migrations`); API açılırken uygulanır (<see cref="InfrastructureServices.InitializeAsync"/>).
/// </summary>
internal sealed class MatchDbContext(DbContextOptions<MatchDbContext> options) : DbContext(options)
{
    public DbSet<MatchRow> Matches => Set<MatchRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var match = modelBuilder.Entity<MatchRow>();
        match.ToTable("Matches");
        match.HasKey(m => m.Id);
        // SQLite uzunluk uygulamaz; sınırlar şemanın belgesi ve ileride PostgreSQL'e geçişte gerçek kısıt olur.
        match.Property(m => m.Id).HasMaxLength(22);
        match.Property(m => m.Lobby).HasMaxLength(22);
        match.Property(m => m.BlueTeam).HasMaxLength(1);
        match.Property(m => m.EngineVersion).HasMaxLength(32);
        // Saklama süresi silmesi oynanma anına göre tarar.
        match.HasIndex(m => m.PlayedAt);
    }
}

/// <summary>
/// Sadece `dotnet ef migrations add` için (tasarım zamanı): uygulamayı ayağa kaldırmadan şemayı okur.
/// Bağlantı dizisi kullanılmaz (migration üretmek veritabanına bağlanmaz).
/// </summary>
internal sealed class MatchDbContextDesignFactory : IDesignTimeDbContextFactory<MatchDbContext>
{
    public MatchDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MatchDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
