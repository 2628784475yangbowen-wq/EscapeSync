using Microsoft.EntityFrameworkCore;

namespace EscapeSync.Server.Data;

/// <summary>
/// Repository pattern abstraction over persisted game records.
/// Lets the game engine persist results without depending on EF Core directly.
/// </summary>
public interface IGameRecordRepository
{
    Task AddAsync(GameRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<GameRecord>> GetRecentAsync(int count, CancellationToken ct = default);
}

public class GameRecordRepository : IGameRecordRepository
{
    private readonly GameDbContext _db;
    public GameRecordRepository(GameDbContext db) => _db = db;

    public async Task AddAsync(GameRecord record, CancellationToken ct = default)
    {
        _db.GameRecords.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GameRecord>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        return await _db.GameRecords
            .OrderByDescending(r => r.EndedAt)
            .Take(count)
            .ToListAsync(ct);
    }
}
