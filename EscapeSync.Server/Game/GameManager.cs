using System.Collections.Concurrent;
using System.Text;
using EscapeSync.GameLogic;
using EscapeSync.Server.Data;
using EscapeSync.Shared;
using Microsoft.AspNetCore.SignalR;
using EscapeSync.Server.Hubs;

namespace EscapeSync.Server.Game;

/// <summary>
/// Singleton that owns every live Room and mediates all hub-driven mutations.
/// Also responsible for pushing authoritative snapshots and per-role views back to clients.
/// </summary>
public class GameManager
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _connectionToRoom = new(); // connId -> roomCode

    private readonly IHubContext<GameHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GameManager> _logger;

    public GameManager(IHubContext<GameHub> hub, IServiceScopeFactory scopeFactory, ILogger<GameManager> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IReadOnlyCollection<Room> Rooms => _rooms.Values.ToList();

    // -----------------------------------------------------------------
    // Room lifecycle
    // -----------------------------------------------------------------

    public Room CreateRoom()
    {
        // Generate a compact human-readable code. Retry if we hit an (unlikely) collision.
        for (var i = 0; i < 10; i++)
        {
            var code = NewRoomCode();
            var room = new Room(code);
            if (_rooms.TryAdd(code, room)) return room;
        }
        throw new InvalidOperationException("Could not allocate a unique room code.");
    }

    public bool TryGetRoom(string code, out Room? room)
    {
        var ok = _rooms.TryGetValue(code, out var r);
        room = r;
        return ok;
    }

    public Room? RoomForConnection(string connectionId) =>
        _connectionToRoom.TryGetValue(connectionId, out var code) && _rooms.TryGetValue(code, out var r)
            ? r : null;

    // -----------------------------------------------------------------
    // Hub-facing operations. Each acquires the room lock for mutation, then
    // broadcasts authoritative state via SignalR.
    // -----------------------------------------------------------------

    public async Task<JoinResult> JoinAsync(string connectionId, string roomCode, string nickname)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return new JoinResult(false, "Room not found.", null, null);

        await room.Sync.WaitAsync();
        PlayerRole? role;
        string? err;
        try
        {
            role = room.TryAddPlayer(connectionId, nickname, out err);
            if (role is null) return new JoinResult(false, err, null, null);
            _connectionToRoom[connectionId] = room.Code;
        }
        finally { room.Sync.Release(); }

        await _hub.Groups.AddToGroupAsync(connectionId, room.Code);
        await BroadcastAsync(room);
        return new JoinResult(true, null, room.Code, role);
    }

    public async Task StartAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var caller) || !caller.IsHost) return;
            if (!room.CanStart()) return;
            room.Start();
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task PressColorAsync(string connectionId, LockColor color)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.PressColor(p, color);
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task ClearGuessAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.ClearGuess(p);
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task SubmitGuessAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.SubmitGuess(p);
        }
        finally { room.Sync.Release(); }
        await PersistIfFinishedAsync(room);
        await BroadcastAsync(room);
    }

    public async Task ActivateAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.Activate(p);
        }
        finally { room.Sync.Release(); }
        await PersistIfFinishedAsync(room);
        await BroadcastAsync(room);
    }

    public async Task PushDoorDigitAsync(string connectionId, int digit)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.PushDoorDigit(p, digit);
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task ClearDoorEntryAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.ClearDoorEntry(p);
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task SubmitDoorEntryAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.SubmitDoorEntry(p);
        }
        finally { room.Sync.Release(); }
        await PersistIfFinishedAsync(room);
        await BroadcastAsync(room);
    }

    public async Task RequestHintAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.RequestHint(p);
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task SendChatAsync(string connectionId, string text)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;
        await room.Sync.WaitAsync();
        try
        {
            if (!room.Players.TryGetValue(connectionId, out var p)) return;
            room.AddChat(p, text);
        }
        finally { room.Sync.Release(); }
        await BroadcastAsync(room);
    }

    public async Task LeaveAsync(string connectionId)
    {
        var room = RoomForConnection(connectionId);
        if (room is null) return;

        await room.Sync.WaitAsync();
        bool removeRoom;
        try
        {
            room.RemovePlayer(connectionId);
            _connectionToRoom.TryRemove(connectionId, out _);
            removeRoom = room.Players.Count == 0;
        }
        finally { room.Sync.Release(); }

        await _hub.Groups.RemoveFromGroupAsync(connectionId, room.Code);

        if (removeRoom)
        {
            _rooms.TryRemove(room.Code, out _);
            return;
        }

        await PersistIfFinishedAsync(room);
        await BroadcastAsync(room);
    }

    // -----------------------------------------------------------------
    // Ticker hook (called by GameTickerService while holding no locks).
    // -----------------------------------------------------------------

    internal async Task TickAllAsync(int deltaMilliseconds)
    {
        foreach (var room in _rooms.Values)
        {
            bool changed = false;
            GameStage stageBefore;

            await room.Sync.WaitAsync();
            try
            {
                stageBefore = room.Stage;
                if (stageBefore is GameStage.Puzzle1 or GameStage.Puzzle2 or GameStage.Puzzle3)
                {
                    room.Tick(deltaMilliseconds);
                    changed = true;
                }
            }
            finally { room.Sync.Release(); }

            if (changed)
            {
                await PersistIfFinishedAsync(room);
                await BroadcastAsync(room);
            }
        }
    }

    // -----------------------------------------------------------------
    // Broadcast helpers
    // -----------------------------------------------------------------

    public async Task BroadcastAsync(Room room)
    {
        // Snapshot is identical for all players in the room.
        var snapshot = await SnapshotAsync(room);
        await _hub.Clients.Group(room.Code).SendAsync(HubEvents.RoomState, snapshot);

        // Role-specific views are sent only to the relevant connection.
        // Only emit role views while a puzzle is active.
        if (snapshot.Stage is GameStage.Puzzle1 or GameStage.Puzzle2 or GameStage.Puzzle3)
        {
            List<(string ConnId, RoleViewDto View)> views;
            await room.Sync.WaitAsync();
            try
            {
                views = room.Players.Values
                    .Select(p => (p.ConnectionId, room.ViewForRole(p.Role)))
                    .ToList();
            }
            finally { room.Sync.Release(); }

            foreach (var (connId, view) in views)
                await _hub.Clients.Client(connId).SendAsync(HubEvents.RoleView, view);
        }
    }

    private async Task<RoomStateDto> SnapshotAsync(Room room)
    {
        await room.Sync.WaitAsync();
        try { return room.ToDto(); }
        finally { room.Sync.Release(); }
    }

    // -----------------------------------------------------------------
    // Persistence (Repository pattern)
    // -----------------------------------------------------------------

    private async Task PersistIfFinishedAsync(Room room)
    {
        GameStage stage;
        GameRecord? record = null;
        await room.Sync.WaitAsync();
        try
        {
            stage = room.Stage;
            if (!room.ResultPersisted
                && (stage == GameStage.Won || stage == GameStage.Lost)
                && room.EndedAt is DateTime ended
                && room.StartedAt is DateTime started)
            {
                var nicknames = string.Join(",", room.Players.Values
                    .OrderBy(p => (int)p.Role)
                    .Select(p => p.Nickname));
                record = new GameRecord
                {
                    RoomCode = room.Code,
                    PlayerNicknames = nicknames,
                    Won = stage == GameStage.Won,
                    DurationSeconds = (int)(ended - started).TotalSeconds,
                    HintsUsed = room.HintsUsedTotal,
                    LivesLost = room.LivesLostTotal,
                    EndedAt = ended
                };
                room.ResultPersisted = true;
            }
        }
        finally { room.Sync.Release(); }

        if (record is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IGameRecordRepository>();
            await repo.AddAsync(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist game record for room {Code}", room.Code);
        }
    }

    // -----------------------------------------------------------------
    // Utilities
    // -----------------------------------------------------------------

    private static string NewRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no lookalikes
        var sb = new StringBuilder(6);
        for (int i = 0; i < 6; i++) sb.Append(alphabet[Random.Shared.Next(alphabet.Length)]);
        return sb.ToString();
    }
}
