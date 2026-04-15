using EscapeSync.Shared;

namespace EscapeSync.Server.Game;

/// <summary>
/// Authoritative in-memory state for a single EscapeSync session.
/// All mutations must be performed while holding <see cref="Sync"/> to avoid races
/// between hub calls and the background ticker.
/// </summary>
public class Room
{
    // --- Tunables ---
    public const int RequiredPlayers = 3;
    public const int StartingLives = 3;
    public const int StartingHints = 3;
    public const int GameDurationSeconds = 10 * 60; // 10-minute MVP timer
    public const int Puzzle1SlotCount = 4;

    public string Code { get; }
    public GameStage Stage { get; private set; } = GameStage.Lobby;
    public int LivesRemaining { get; private set; } = StartingLives;
    public int HintsRemaining { get; private set; } = StartingHints;
    public int SecondsRemaining { get; private set; } = GameDurationSeconds;

    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    /// <summary>Set once the GameRecord has been written so we never double-persist.</summary>
    public bool ResultPersisted { get; set; }

    /// <summary>ConnectionId -> Player. Slot preserved via insertion order for role assignment.</summary>
    public readonly Dictionary<string, Player> Players = new();

    public readonly List<ChatMessageDto> ChatLog = new();

    public string? LastHintText { get; private set; }
    public string? LastFeedback { get; private set; }

    public Puzzle1State Puzzle1 { get; private set; } = Puzzle1State.Generate();
    public Puzzle2State Puzzle2 { get; private set; } = Puzzle2State.Generate();

    /// <summary>Counters used when writing the final GameRecord.</summary>
    public int HintsUsedTotal { get; private set; }
    public int LivesLostTotal { get; private set; }

    /// <summary>Acquired on every mutation. Exposed to callers that want to bundle multiple ops.</summary>
    public readonly SemaphoreSlim Sync = new(1, 1);

    public Room(string code) { Code = code; }

    // -----------------------------------------------------------------
    // Player lifecycle
    // -----------------------------------------------------------------

    /// <summary>Attempts to add a player. Returns assigned role on success.</summary>
    public PlayerRole? TryAddPlayer(string connectionId, string nickname, out string? error)
    {
        error = null;
        if (Stage != GameStage.Lobby) { error = "Game already in progress."; return null; }
        if (Players.Count >= RequiredPlayers) { error = "Room is full."; return null; }
        if (Players.Values.Any(p => p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Nickname already taken in this room."; return null;
        }

        // Role is assigned by join order: 0=Locksmith, 1=Cryptographer, 2=Operator.
        var role = (PlayerRole)Players.Count;
        var isHost = Players.Count == 0;
        Players[connectionId] = new Player(connectionId, nickname, role, isHost);
        return role;
    }

    public Player? RemovePlayer(string connectionId)
    {
        if (!Players.Remove(connectionId, out var removed)) return null;

        // If the game was in progress and a player leaves, end the session as a loss.
        if (Stage is GameStage.Puzzle1 or GameStage.Puzzle2)
        {
            Stage = GameStage.Lost;
            EndedAt = DateTime.UtcNow;
            LastFeedback = $"{removed.Nickname} left the room. Mission aborted.";
        }
        return removed;
    }

    // -----------------------------------------------------------------
    // Game progression
    // -----------------------------------------------------------------

    public bool CanStart() => Stage == GameStage.Lobby && Players.Count == RequiredPlayers;

    public void Start()
    {
        if (!CanStart()) throw new InvalidOperationException("Cannot start this room.");
        Stage = GameStage.Puzzle1;
        StartedAt = DateTime.UtcNow;
        SecondsRemaining = GameDurationSeconds;
        LastFeedback = "Puzzle 1: Crack the combination lock.";
    }

    /// <summary>Advances the countdown. Transitions to Lost if it hits zero.</summary>
    public void Tick(int deltaMilliseconds)
    {
        if (Stage is not (GameStage.Puzzle1 or GameStage.Puzzle2)) return;

        // Timer: decrement once per whole second that elapses.
        var deltaSeconds = deltaMilliseconds / 1000;
        if (deltaSeconds > 0)
        {
            SecondsRemaining = Math.Max(0, SecondsRemaining - deltaSeconds);
            if (SecondsRemaining == 0)
            {
                Stage = GameStage.Lost;
                EndedAt = DateTime.UtcNow;
                LastFeedback = "Time's up! The team failed to escape.";
                return;
            }
        }

        // Puzzle 2: advance the needle while it's the active puzzle.
        if (Stage == GameStage.Puzzle2)
            Puzzle2.Advance(deltaMilliseconds);
    }

    // -----------------------------------------------------------------
    // Puzzle 1 actions
    // -----------------------------------------------------------------

    public void PressColor(Player player, LockColor color)
    {
        if (Stage != GameStage.Puzzle1) return;
        if (player.Role != PlayerRole.Locksmith) return;
        if (Puzzle1.CurrentGuess.Count >= Puzzle1SlotCount) return;
        Puzzle1.CurrentGuess.Add(color);
        LastFeedback = $"{player.Nickname} pressed {color}.";
    }

    public void ClearGuess(Player player)
    {
        if (Stage != GameStage.Puzzle1) return;
        if (player.Role != PlayerRole.Operator) return;
        Puzzle1.CurrentGuess.Clear();
        LastFeedback = $"{player.Nickname} cleared the input.";
    }

    public void SubmitGuess(Player player)
    {
        if (Stage != GameStage.Puzzle1) return;
        if (player.Role != PlayerRole.Operator) return;
        if (Puzzle1.CurrentGuess.Count != Puzzle1SlotCount) return;

        // Translate current color guess through the cipher into digits and compare.
        var digits = Puzzle1.CurrentGuess.Select(c => Puzzle1.Cipher[c]).ToList();
        if (digits.SequenceEqual(Puzzle1.TargetDigits))
        {
            Stage = GameStage.Puzzle2;
            LastFeedback = "The lock clicks open! Puzzle 2: Activate the mechanism in the safe zone.";
        }
        else
        {
            LivesRemaining--;
            LivesLostTotal++;
            Puzzle1.CurrentGuess.Clear();
            if (LivesRemaining <= 0)
            {
                Stage = GameStage.Lost;
                EndedAt = DateTime.UtcNow;
                LastFeedback = "Out of lives. The mission failed.";
            }
            else
            {
                LastFeedback = $"Wrong combination. {LivesRemaining} lives remaining.";
            }
        }
    }

    // -----------------------------------------------------------------
    // Puzzle 2 actions
    // -----------------------------------------------------------------

    public void Activate(Player player)
    {
        if (Stage != GameStage.Puzzle2) return;
        if (player.Role != PlayerRole.Operator) return;

        if (Puzzle2.NeedleInSafeZone())
        {
            Stage = GameStage.Won;
            EndedAt = DateTime.UtcNow;
            LastFeedback = "The mechanism engages. You escaped!";
        }
        else
        {
            LivesRemaining--;
            LivesLostTotal++;
            if (LivesRemaining <= 0)
            {
                Stage = GameStage.Lost;
                EndedAt = DateTime.UtcNow;
                LastFeedback = "The mechanism jams. Mission failed.";
            }
            else
            {
                LastFeedback = $"Missed! Needle at {Puzzle2.NeedlePosition}. {LivesRemaining} lives remaining.";
            }
        }
    }

    // -----------------------------------------------------------------
    // Hints & chat
    // -----------------------------------------------------------------

    public void RequestHint(Player player)
    {
        if (Stage is not (GameStage.Puzzle1 or GameStage.Puzzle2)) return;
        if (HintsRemaining <= 0) { LastHintText = "No hints remaining."; return; }

        HintsRemaining--;
        HintsUsedTotal++;

        LastHintText = Stage == GameStage.Puzzle1
            ? $"Hint: the first target digit is {Puzzle1.TargetDigits[0]}."
            : $"Hint: the safe zone is near {(Puzzle2.SafeZoneStart + Puzzle2.SafeZoneEnd) / 2}.";
        LastFeedback = $"{player.Nickname} requested a hint.";
    }

    public void AddChat(Player player, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // Trim overly long messages to keep broadcast payloads bounded.
        if (text.Length > 280) text = text[..280];
        ChatLog.Add(new ChatMessageDto(player.Nickname, text, DateTime.UtcNow));
        // Keep only the last 50 messages to bound memory.
        if (ChatLog.Count > 50) ChatLog.RemoveAt(0);
    }

    // -----------------------------------------------------------------
    // Projections
    // -----------------------------------------------------------------

    public RoomStateDto ToDto()
    {
        var players = Players.Values
            .OrderBy(p => (int)p.Role)
            .Select(p => new PlayerDto(p.ConnectionId, p.Nickname, p.Role, p.IsHost))
            .ToList();
        return new RoomStateDto(
            Code,
            Stage,
            LivesRemaining,
            HintsRemaining,
            SecondsRemaining,
            players,
            ChatLog.ToList(),
            LastHintText,
            LastFeedback);
    }

    public RoleViewDto ViewForRole(PlayerRole role) => Stage switch
    {
        GameStage.Puzzle1 => role switch
        {
            PlayerRole.Locksmith => new RoleViewDto(role, Stage,
                P1Locksmith: new Puzzle1LocksmithView(Puzzle1.CurrentGuess.ToList(), Puzzle1SlotCount)),
            PlayerRole.Cryptographer => new RoleViewDto(role, Stage,
                P1Cryptographer: new Puzzle1CryptographerView(Puzzle1.TargetDigits.ToList(), Puzzle1.Cipher)),
            PlayerRole.Operator => new RoleViewDto(role, Stage,
                P1Operator: new Puzzle1OperatorView(
                    Puzzle1.CurrentGuess.ToList(),
                    Puzzle1SlotCount,
                    Puzzle1.CurrentGuess.Count == Puzzle1SlotCount)),
            _ => new RoleViewDto(role, Stage)
        },
        GameStage.Puzzle2 => role switch
        {
            PlayerRole.Locksmith => new RoleViewDto(role, Stage,
                P2Locksmith: new Puzzle2LocksmithView(Puzzle2.SafeZoneStart, Puzzle2.SafeZoneEnd)),
            PlayerRole.Cryptographer => new RoleViewDto(role, Stage,
                P2Cryptographer: new Puzzle2CryptographerView(Puzzle2.NeedlePosition)),
            PlayerRole.Operator => new RoleViewDto(role, Stage,
                P2Operator: new Puzzle2OperatorView(true)),
            _ => new RoleViewDto(role, Stage)
        },
        _ => new RoleViewDto(role, Stage)
    };
}

public class Player
{
    public string ConnectionId { get; }
    public string Nickname { get; }
    public PlayerRole Role { get; }
    public bool IsHost { get; }
    public Player(string connectionId, string nickname, PlayerRole role, bool isHost)
    { ConnectionId = connectionId; Nickname = nickname; Role = role; IsHost = isHost; }
}
