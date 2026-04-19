using EscapeSync.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace EscapeSync.Client.Services;

/// <summary>
/// Singleton client-side wrapper around the SignalR connection and the last-known room state.
/// Pages subscribe to <see cref="StateChanged"/> to re-render on push notifications.
/// </summary>
public class GameClient : IAsyncDisposable
{
    private readonly ServerEndpoint _endpoint;
    private HubConnection? _connection;

    public RoomStateDto? Room { get; private set; }
    public RoleViewDto? RoleView { get; private set; }
    public PlayerRole? MyRole { get; private set; }
    public string? MyNickname { get; private set; }

    /// <summary>Raised on UI thread whenever Room or RoleView is refreshed.</summary>
    public event Action? StateChanged;

    public GameClient(ServerEndpoint endpoint) { _endpoint = endpoint; }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    private async Task EnsureConnectedAsync()
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected) return;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_endpoint.BaseUrl}/gamehub")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<RoomStateDto>(HubEvents.RoomState, dto =>
        {
            Room = dto;
            StateChanged?.Invoke();
        });

        _connection.On<RoleViewDto>(HubEvents.RoleView, dto =>
        {
            RoleView = dto;
            StateChanged?.Invoke();
        });

        await _connection.StartAsync();
    }

    public async Task<JoinResult> CreateRoomAsync(string nickname)
    {
        await EnsureConnectedAsync();
        MyNickname = nickname;
        var result = await _connection!.InvokeAsync<JoinResult>(HubMethods.CreateRoom, nickname);
        if (result.Success) MyRole = result.AssignedRole;
        return result;
    }

    public async Task<JoinResult> JoinRoomAsync(string roomCode, string nickname)
    {
        await EnsureConnectedAsync();
        MyNickname = nickname;
        var result = await _connection!.InvokeAsync<JoinResult>(HubMethods.JoinRoom, roomCode, nickname);
        if (result.Success) MyRole = result.AssignedRole;
        return result;
    }

    public Task StartGameAsync() => Invoke(HubMethods.StartGame);
    public Task PressColorAsync(LockColor c) => Invoke(HubMethods.PressColor, c);
    public Task ClearGuessAsync() => Invoke(HubMethods.ClearGuess);
    public Task SubmitGuessAsync() => Invoke(HubMethods.SubmitGuess);
    public Task ActivateAsync() => Invoke(HubMethods.Activate);
    public Task PushDoorDigitAsync(int digit) => Invoke(HubMethods.PushDoorDigit, digit);
    public Task ClearDoorEntryAsync() => Invoke(HubMethods.ClearDoorEntry);
    public Task SubmitDoorEntryAsync() => Invoke(HubMethods.SubmitDoorEntry);
    public Task RequestHintAsync() => Invoke(HubMethods.RequestHint);
    public Task SendChatAsync(string text) => Invoke(HubMethods.SendChat, text);
    public Task LeaveRoomAsync() => Invoke(HubMethods.LeaveRoom);

    private async Task Invoke(string method, object? arg = null)
    {
        if (_connection?.State != HubConnectionState.Connected) return;
        if (arg is null) await _connection.SendAsync(method);
        else await _connection.SendAsync(method, arg);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }

    public void Reset()
    {
        Room = null;
        RoleView = null;
        MyRole = null;
        StateChanged?.Invoke();
    }
}
