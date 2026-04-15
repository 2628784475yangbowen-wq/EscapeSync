namespace EscapeSync.Shared;

/// <summary>
/// The three fixed roles a player can hold in EscapeSync.
/// Each role sees a different subset of each puzzle's information.
/// </summary>
public enum PlayerRole
{
    Locksmith = 0,
    Cryptographer = 1,
    Operator = 2
}

/// <summary>
/// High-level stage of a single EscapeSync session.
/// </summary>
public enum GameStage
{
    Lobby = 0,
    Puzzle1 = 1,
    Puzzle2 = 2,
    Won = 3,
    Lost = 4
}

/// <summary>
/// The colors used on the Puzzle 1 combination lock buttons.
/// </summary>
public enum LockColor
{
    Red = 0,
    Blue = 1,
    Green = 2,
    Yellow = 3
}
