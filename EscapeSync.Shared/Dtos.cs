namespace EscapeSync.Shared;

/// <summary>
/// Public information about a connected player.
/// </summary>
public record PlayerDto(string ConnectionId, string Nickname, PlayerRole Role, bool IsHost);

/// <summary>
/// A chat message in the room.
/// </summary>
public record ChatMessageDto(string Nickname, string Text, DateTime SentAt);

/// <summary>
/// What the Locksmith can see for Puzzle 1.
/// Sees only the colored buttons and colored pips of the current guess.
/// Does not see digits or the target sequence.
/// </summary>
public record Puzzle1LocksmithView(
    IReadOnlyList<LockColor> CurrentGuess,
    int SlotCount);

/// <summary>
/// What the Cryptographer can see for Puzzle 1.
/// Sees the target digit sequence and the cipher table mapping color->digit.
/// Does not see the colored buttons or current guess.
/// </summary>
public record Puzzle1CryptographerView(
    IReadOnlyList<int> TargetDigits,
    IReadOnlyDictionary<LockColor, int> CipherTable);

/// <summary>
/// What the Operator can see for Puzzle 1.
/// Sees the colored pips of the current guess (same visual as Locksmith)
/// plus SUBMIT / CLEAR controls. Does not see the cipher or target digits.
/// </summary>
public record Puzzle1OperatorView(
    IReadOnlyList<LockColor> CurrentGuess,
    int SlotCount,
    bool CanSubmit);

/// <summary>
/// What the Locksmith can see for Puzzle 2.
/// Sees the safe-zone bounds (start and end along a 0-100 scale).
/// Does not see the current needle position.
/// </summary>
public record Puzzle2LocksmithView(int SafeZoneStart, int SafeZoneEnd);

/// <summary>
/// What the Cryptographer can see for Puzzle 2.
/// Sees the current needle position (0-100). Does not see the safe-zone bounds.
/// </summary>
public record Puzzle2CryptographerView(int NeedlePosition);

/// <summary>
/// What the Operator can see for Puzzle 2.
/// Sees only the ACTIVATE button and has no direct sensor info.
/// </summary>
public record Puzzle2OperatorView(bool CanActivate);

/// <summary>
/// Snapshot of the whole room sent to every client after each state change.
/// Per-role puzzle views are delivered separately via RoleView messages.
/// </summary>
public record RoomStateDto(
    string RoomCode,
    GameStage Stage,
    int LivesRemaining,
    int HintsRemaining,
    int SecondsRemaining,
    IReadOnlyList<PlayerDto> Players,
    IReadOnlyList<ChatMessageDto> ChatLog,
    string? LastHintText,
    string? LastFeedback);

/// <summary>
/// A role-specific view payload. Exactly one of the puzzle-specific properties is non-null.
/// </summary>
public record RoleViewDto(
    PlayerRole Role,
    GameStage Stage,
    Puzzle1LocksmithView? P1Locksmith = null,
    Puzzle1CryptographerView? P1Cryptographer = null,
    Puzzle1OperatorView? P1Operator = null,
    Puzzle2LocksmithView? P2Locksmith = null,
    Puzzle2CryptographerView? P2Cryptographer = null,
    Puzzle2OperatorView? P2Operator = null);

/// <summary>
/// Result of a join attempt returned to the caller.
/// </summary>
public record JoinResult(bool Success, string? ErrorMessage, string? RoomCode, PlayerRole? AssignedRole);
