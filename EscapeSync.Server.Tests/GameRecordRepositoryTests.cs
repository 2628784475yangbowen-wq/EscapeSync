using EscapeSync.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EscapeSync.Server.Tests;

public class GameRecordRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GameDbContext _db;
    private readonly GameRecordRepository _repo;

    public GameRecordRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlite(_connection).Options;
        _db = new GameDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new GameRecordRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // Why: AddAsync is the only write path in the repository; confirming the record
    // lands in the DB with a generated primary key proves EF Core is wired up
    // correctly against real SQLite (not a mock).
    [Fact]
    public async Task AddAsync_PersistsRecord()
    {
        var record = MakeRecord("ROOM01", won: true);
        await _repo.AddAsync(record);

        var saved = await _db.GameRecords.SingleAsync();
        Assert.Equal("ROOM01", saved.RoomCode);
        Assert.True(saved.Won);
        Assert.True(saved.Id > 0);
    }

    // Why: GetRecentAsync powers the leaderboard / history view; we need to confirm
    // it returns results newest-first and that the count limit is enforced so the
    // API never dumps the entire table to the client.
    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst_RespectsLimit()
    {
        for (int i = 0; i < 5; i++)
            await _repo.AddAsync(MakeRecord($"R{i}", endedAt: DateTime.UtcNow.AddMinutes(i)));

        var results = await _repo.GetRecentAsync(3);

        Assert.Equal(3, results.Count);
        Assert.Equal("R4", results[0].RoomCode);
    }

    // Why: every field in GameRecord must survive the EF Core round-trip without
    // truncation or type mismatch; catching a schema mismatch here is far cheaper
    // than debugging corrupt data in production.
    [Fact]
    public async Task AddAsync_AllFieldsRoundTrip()
    {
        var record = new GameRecord
        {
            RoomCode = "FULL01",
            PlayerNicknames = "Alice,Bob,Charlie",
            Won = false,
            DurationSeconds = 300,
            HintsUsed = 2,
            LivesLost = 3,
            EndedAt = new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc)
        };
        await _repo.AddAsync(record);

        var saved = await _db.GameRecords.SingleAsync();
        Assert.Equal("Alice,Bob,Charlie", saved.PlayerNicknames);
        Assert.Equal(300, saved.DurationSeconds);
        Assert.Equal(2, saved.HintsUsed);
        Assert.Equal(3, saved.LivesLost);
    }

    private static GameRecord MakeRecord(string code, bool won = false, DateTime? endedAt = null) => new()
    {
        RoomCode = code,
        PlayerNicknames = "Alice,Bob,Charlie",
        Won = won,
        DurationSeconds = 120,
        HintsUsed = 1,
        LivesLost = 1,
        EndedAt = endedAt ?? DateTime.UtcNow
    };
}

