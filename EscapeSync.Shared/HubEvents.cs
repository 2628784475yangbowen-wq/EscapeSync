namespace EscapeSync.Shared;

/// <summary>
/// Well-known names of SignalR client-side methods that the server invokes.
/// Kept here so client and server agree at compile time.
/// </summary>
public static class HubEvents
{
    public const string RoomState = "RoomState";
    public const string RoleView = "RoleView";
    public const string JoinResult = "JoinResult";
    public const string Kicked = "Kicked";
}

/// <summary>
/// Well-known names of SignalR server-side hub methods that clients invoke.
/// </summary>
public static class HubMethods
{
    public const string CreateRoom = "CreateRoom";
    public const string JoinRoom = "JoinRoom";
    public const string StartGame = "StartGame";
    public const string PressColor = "PressColor";
    public const string ClearGuess = "ClearGuess";
    public const string SubmitGuess = "SubmitGuess";
    public const string Activate = "Activate";
    public const string RequestHint = "RequestHint";
    public const string SendChat = "SendChat";
    public const string LeaveRoom = "LeaveRoom";
}
