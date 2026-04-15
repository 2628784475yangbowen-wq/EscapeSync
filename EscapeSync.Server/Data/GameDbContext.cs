using Microsoft.EntityFrameworkCore;

namespace EscapeSync.Server.Data;

/// <summary>
/// EF Core context for EscapeSync. Currently persists only finished game records;
/// live in-progress state lives in memory inside <see cref="Game.GameManager"/>.
/// </summary>
public class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

    public DbSet<GameRecord> GameRecords => Set<GameRecord>();
}
