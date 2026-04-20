using EscapeSync.Server.Data;
using EscapeSync.Server.Game;
using EscapeSync.Server.Hubs;
using EscapeSync.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace EscapeSync.Server.Tests;

public class GameManagerTests : IDisposable
{
    private readonly GameManager _manager;
    private readonly Mock<IClientProxy> _groupProxyMock;
    private readonly Mock<IGroupManager> _groupManagerMock;
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public GameManagerTests()
    {
        var hubContextMock = new Mock<IHubContext<GameHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        _groupProxyMock = new Mock<IClientProxy>();
        _groupManagerMock = new Mock<IGroupManager>();

        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupProxyMock.Object);
        hubClientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(new Mock<ISingleClientProxy>().Object);
        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubContextMock.Setup(h => h.Groups).Returns(_groupManagerMock.Object);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<GameDbContext>(opt => opt.UseSqlite(_connection));
        services.AddScoped<IGameRecordRepository, GameRecordRepository>();
        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
            scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.EnsureCreated();

        _manager = new GameManager(
            hubContextMock.Object,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new Mock<ILogger<GameManager>>().Object);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    // Why: JoinAsync must add the connection to the SignalR group AND broadcast a
    // RoomStateDto to the group — without both, the new player's UI would never
    // receive the initial room snapshot.
    [Fact]
    public async Task JoinAsync_ValidRoom_SucceedsAndBroadcasts()
    {
        var room = _manager.CreateRoom();

        var result = await _manager.JoinAsync("conn1", room.Code, "Alice");

        Assert.True(result.Success);
        Assert.Equal(PlayerRole.Locksmith, result.AssignedRole);
        _groupManagerMock.Verify(g => g.AddToGroupAsync("conn1", room.Code, default), Times.Once);
        _groupProxyMock.Verify(p => p.SendCoreAsync(
            HubEvents.RoomState,
            It.Is<object?[]>(args => args.Length == 1 && args[0] is RoomStateDto),
            default), Times.AtLeastOnce);
    }

    // Why: when the last player leaves, the room must be fully removed from the
    // manager's dictionary so empty rooms don't accumulate and waste memory during
    // long server uptimes.
    [Fact]
    public async Task LeaveAsync_EmptyRoom_RemovedFromManager()
    {
        var room = _manager.CreateRoom();
        await _manager.JoinAsync("c1", room.Code, "Alice");

        await _manager.LeaveAsync("c1");

        Assert.DoesNotContain(room, _manager.Rooms);
        _groupManagerMock.Verify(g => g.RemoveFromGroupAsync("c1", room.Code, default), Times.Once);
    }

    // Why: the full win path is the most important integration path; we verify
    // that the manager correctly chains Puzzle 1 → Puzzle 2 → Won and that
    // PersistIfFinishedAsync writes exactly one GameRecord to SQLite.
    [Fact]
    public async Task FullGameWin_PersistsGameRecord()
    {
        var room = _manager.CreateRoom();
        await _manager.JoinAsync("c1", room.Code, "Alice");
        await _manager.JoinAsync("c2", room.Code, "Bob");
        await _manager.JoinAsync("c3", room.Code, "Charlie");
        await _manager.StartAsync("c1");

        // Solve Puzzle 1
        var correctColors = room.Puzzle1.TargetDigits
            .Select(d => room.Puzzle1.Cipher.First(kv => kv.Value == d).Key).ToList();
        foreach (var c in correctColors)
            await _manager.PressColorAsync("c1", c);
        await _manager.SubmitGuessAsync("c3");

        // Solve Puzzle 2
        for (int i = 0; i < 10000 && !room.Puzzle2.NeedleInSafeZone(); i++)
            room.Puzzle2.Advance(10);
        await _manager.ActivateAsync("c3");

        Assert.Equal(GameStage.Won, room.Stage);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        var record = await db.GameRecords.SingleOrDefaultAsync();
        Assert.NotNull(record);
        Assert.True(record.Won);
        Assert.Equal(room.Code, record.RoomCode);
    }
}
