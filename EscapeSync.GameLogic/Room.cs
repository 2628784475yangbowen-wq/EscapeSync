using EscapeSync.Shared;

namespace EscapeSync.GameLogic;

public sealed class Room
{
    public const int RequiredPlayers = 3;
    public const int StartingLives = 3;
    public const int StartingHints = 3;
    public const int GameDurationSeconds = 30 * 60;

    public string Code { get; }
    public GameStage Stage { get; private set; } = GameStage.Lobby;
    public int LivesRemaining { get; private set; } = StartingLives;
    public int HintsRemaining { get; private set; } = StartingHints;
    public int SecondsRemaining { get; private set; } = GameDurationSeconds;

    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    public bool ResultPersisted { get; set; }

    public readonly Dictionary<string, Player> Players = new();
    public readonly List<ChatMessageDto> ChatLog = new();

    public string? LastHintText { get; private set; }
    public string? LastFeedback { get; private set; }

    public Puzzle1State Puzzle1 { get; private set; } = Puzzle1State.Generate();
    public Puzzle2State Puzzle2 { get; private set; } = Puzzle2State.Generate();
    public Puzzle3State Puzzle3 { get; private set; } = Puzzle3State.Generate();

    public int HintsUsedTotal { get; private set; }
    public int LivesLostTotal { get; private set; }

    private int _timerRemainderMs;

    public readonly SemaphoreSlim Sync = new(1, 1);

    public Room(string code) => Code = code;

    public PlayerRole? TryAddPlayer(string connectionId, string nickname, out string? error)
    {
        error = null;
        if (Stage != GameStage.Lobby) { error = "Game already in progress."; return null; }
        if (Players.Count >= RequiredPlayers) { error = "Room is full."; return null; }
        if (Players.Values.Any(p => p.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Nickname already taken in this room."; return null;
        }

        var role = (PlayerRole)Players.Count;
        var isHost = Players.Count == 0;
        Players[connectionId] = new Player(connectionId, nickname, role, isHost);
        return role;
    }

    public Player? RemovePlayer(string connectionId)
    {
        if (!Players.Remove(connectionId, out var removed)) return null;

        if (Stage is GameStage.Puzzle1 or GameStage.Puzzle2 or GameStage.Puzzle3)
        {
            Stage = GameStage.Lost;
            EndedAt = DateTime.UtcNow;
            LastFeedback = $"{removed.Nickname} left the room. Mission aborted.";
        }
        return removed;
    }

    public bool CanStart() => Stage == GameStage.Lobby && Players.Count == RequiredPlayers;

    public void Start(Random? rng = null)
    {
        if (!CanStart()) throw new InvalidOperationException("Cannot start this room.");
        Puzzle1 = Puzzle1State.Generate(rng);
        Puzzle2 = Puzzle2State.Generate(rng);
        Puzzle3 = Puzzle3State.Generate(rng);
        Stage = GameStage.Puzzle1;
        StartedAt = DateTime.UtcNow;
        SecondsRemaining = GameDurationSeconds;
        LivesRemaining = StartingLives;
        HintsRemaining = StartingHints;
        LastHintText = null;
        LastFeedback = "Puzzle 1: Crack the combination lock using asymmetric clues.";
        _timerRemainderMs = 0;
    }

    public void Tick(int deltaMilliseconds)
    {
        if (Stage is not (GameStage.Puzzle1 or GameStage.Puzzle2 or GameStage.Puzzle3)) return;
        if (deltaMilliseconds <= 0) return;

        _timerRemainderMs += deltaMilliseconds;
        var wholeSeconds = _timerRemainderMs / 1000;
        if (wholeSeconds > 0)
        {
            _timerRemainderMs %= 1000;
            SecondsRemaining = Math.Max(0, SecondsRemaining - wholeSeconds);
            if (SecondsRemaining == 0)
            {
                Stage = GameStage.Lost;
                EndedAt = DateTime.UtcNow;
                LastFeedback = "Time's up! The team failed to escape.";
                return;
            }
        }

        if (Stage == GameStage.Puzzle2)
            Puzzle2.Advance(deltaMilliseconds);
    }

    public void PressColor(Player player, LockColor color)
    {
        if (Stage != GameStage.Puzzle1) return;
        if (player.Role != PlayerRole.Locksmith) return;
        if (Puzzle1.CurrentGuess.Count >= Puzzle1State.SlotCount) return;
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
        if (Puzzle1.CurrentGuess.Count != Puzzle1State.SlotCount) return;

        var digits = Puzzle1.CurrentGuess.Select(c => Puzzle1.Cipher[c]).ToList();
        if (digits.SequenceEqual(Puzzle1.TargetDigits))
        {
            Stage = GameStage.Puzzle2;
            LastFeedback = "The lock opens. Puzzle 2: activate while the needle is in the safe zone.";
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

    public void Activate(Player player)
    {
        if (Stage != GameStage.Puzzle2) return;
        if (player.Role != PlayerRole.Operator) return;

        if (Puzzle2.NeedleInSafeZone())
        {
            Puzzle3.ClearEntry();
            Stage = GameStage.Puzzle3;
            LastFeedback = "Mechanism aligned. Puzzle 3: enter the four-digit door code (left digits, then right digits).";
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
                LastFeedback = $"Missed. Needle at {Puzzle2.NeedlePosition}. {LivesRemaining} lives remaining.";
            }
        }
    }

    public void PushDoorDigit(Player player, int digit)
    {
        if (Stage != GameStage.Puzzle3) return;
        if (player.Role != PlayerRole.Operator) return;
        Puzzle3.PushDigit(digit);
        LastFeedback = $"{player.Nickname} entered a digit.";
    }

    public void ClearDoorEntry(Player player)
    {
        if (Stage != GameStage.Puzzle3) return;
        if (player.Role != PlayerRole.Operator) return;
        Puzzle3.ClearEntry();
        LastFeedback = $"{player.Nickname} cleared the door entry.";
    }

    public void SubmitDoorEntry(Player player)
    {
        if (Stage != GameStage.Puzzle3) return;
        if (player.Role != PlayerRole.Operator) return;
        if (!Puzzle3.IsComplete()) return;

        if (Puzzle3.MatchesTarget())
        {
            Stage = GameStage.Won;
            EndedAt = DateTime.UtcNow;
            LastFeedback = "Door unlocked. You escaped.";
        }
        else
        {
            LivesRemaining--;
            LivesLostTotal++;
            Puzzle3.ClearEntry();
            if (LivesRemaining <= 0)
            {
                Stage = GameStage.Lost;
                EndedAt = DateTime.UtcNow;
                LastFeedback = "Wrong door code. Mission failed.";
            }
            else
            {
                LastFeedback = $"Wrong door code. {LivesRemaining} lives remaining.";
            }
        }
    }

    public void RequestHint(Player player)
    {
        if (Stage is not (GameStage.Puzzle1 or GameStage.Puzzle2 or GameStage.Puzzle3)) return;
        if (HintsRemaining <= 0) { LastHintText = "No hints remaining."; return; }

        HintsRemaining--;
        HintsUsedTotal++;

        LastHintText = Stage switch
        {
            GameStage.Puzzle1 => $"Hint: the first target digit is {Puzzle1.TargetDigits[0]}.",
            GameStage.Puzzle2 => $"Hint: the safe zone center is near {(Puzzle2.SafeZoneStart + Puzzle2.SafeZoneEnd) / 2}.",
            GameStage.Puzzle3 => $"Hint: the door code starts with {Puzzle3.TargetCode[0]}.",
            _ => "Hint unavailable.",
        };
        LastFeedback = $"{player.Nickname} requested a hint.";
    }

    public void AddChat(Player player, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 280) text = text[..280];
        ChatLog.Add(new ChatMessageDto(player.Nickname, text, DateTime.UtcNow));
        if (ChatLog.Count > 50) ChatLog.RemoveAt(0);
    }

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
                P1Locksmith: new Puzzle1LocksmithView(Puzzle1.CurrentGuess.ToList(), Puzzle1State.SlotCount)),
            PlayerRole.Cryptographer => new RoleViewDto(role, Stage,
                P1Cryptographer: new Puzzle1CryptographerView(Puzzle1.TargetDigits.ToList(), Puzzle1.Cipher)),
            PlayerRole.Operator => new RoleViewDto(role, Stage,
                P1Operator: new Puzzle1OperatorView(
                    Puzzle1.CurrentGuess.ToList(),
                    Puzzle1State.SlotCount,
                    Puzzle1.CurrentGuess.Count == Puzzle1State.SlotCount)),
            _ => new RoleViewDto(role, Stage),
        },
        GameStage.Puzzle2 => role switch
        {
            PlayerRole.Locksmith => new RoleViewDto(role, Stage,
                P2Locksmith: new Puzzle2LocksmithView(Puzzle2.SafeZoneStart, Puzzle2.SafeZoneEnd)),
            PlayerRole.Cryptographer => new RoleViewDto(role, Stage,
                P2Cryptographer: new Puzzle2CryptographerView(Puzzle2.NeedlePosition)),
            PlayerRole.Operator => new RoleViewDto(role, Stage,
                P2Operator: new Puzzle2OperatorView(true)),
            _ => new RoleViewDto(role, Stage),
        },
        GameStage.Puzzle3 => role switch
        {
            PlayerRole.Locksmith => new RoleViewDto(role, Stage,
                P3Locksmith: new Puzzle3LocksmithView(Puzzle3.LeftPair)),
            PlayerRole.Cryptographer => new RoleViewDto(role, Stage,
                P3Cryptographer: new Puzzle3CryptographerView(Puzzle3.RightPair)),
            PlayerRole.Operator => new RoleViewDto(role, Stage,
                P3Operator: BuildP3OperatorView()),
            _ => new RoleViewDto(role, Stage),
        },
        _ => new RoleViewDto(role, Stage),
    };

    private Puzzle3OperatorView BuildP3OperatorView()
    {
        var slots = new List<int?>(4);
        for (var i = 0; i < 4; i++)
            slots.Add(i < Puzzle3.Entry.Count ? Puzzle3.Entry[i] : null);
        return new Puzzle3OperatorView(slots, Puzzle3.IsComplete());
    }
}

public sealed class Player
{
    public string ConnectionId { get; }
    public string Nickname { get; }
    public PlayerRole Role { get; }
    public bool IsHost { get; }

    public Player(string connectionId, string nickname, PlayerRole role, bool isHost)
    {
        ConnectionId = connectionId;
        Nickname = nickname;
        Role = role;
        IsHost = isHost;
    }
}
