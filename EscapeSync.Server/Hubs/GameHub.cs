using EscapeSync.Server.Game;
using EscapeSync.Shared;
using Microsoft.AspNetCore.SignalR;

namespace EscapeSync.Server.Hubs;

/// <summary>
/// SignalR hub — the only entry point clients use to drive game state.
/// All methods delegate to <see cref="GameManager"/>, which is the single source of truth.
/// </summary>
public class GameHub : Hub
{
    private readonly GameManager _manager;
    private readonly ILogger<GameHub> _logger;

    public GameHub(GameManager manager, ILogger<GameHub> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    public async Task<JoinResult> CreateRoom(string nickname)
    {
        var room = _manager.CreateRoom();
        _logger.LogInformation("Created room {Code}", room.Code);
        return await _manager.JoinAsync(Context.ConnectionId, room.Code, nickname);
    }

    public Task<JoinResult> JoinRoom(string roomCode, string nickname)
        => _manager.JoinAsync(Context.ConnectionId, roomCode, nickname);

    public Task StartGame() => _manager.StartAsync(Context.ConnectionId);

    public Task PressColor(LockColor color) => _manager.PressColorAsync(Context.ConnectionId, color);
    public Task ClearGuess() => _manager.ClearGuessAsync(Context.ConnectionId);
    public Task SubmitGuess() => _manager.SubmitGuessAsync(Context.ConnectionId);
    public Task Activate() => _manager.ActivateAsync(Context.ConnectionId);
    public Task PushDoorDigit(int digit) => _manager.PushDoorDigitAsync(Context.ConnectionId, digit);
    public Task ClearDoorEntry() => _manager.ClearDoorEntryAsync(Context.ConnectionId);
    public Task SubmitDoorEntry() => _manager.SubmitDoorEntryAsync(Context.ConnectionId);
    public Task RequestHint() => _manager.RequestHintAsync(Context.ConnectionId);
    public Task SendChat(string text) => _manager.SendChatAsync(Context.ConnectionId, text);
    public Task LeaveRoom() => _manager.LeaveAsync(Context.ConnectionId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _manager.LeaveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
