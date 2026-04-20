using EscapeSync.Server.Game;
using EscapeSync.Shared;

namespace EscapeSync.Server.Tests;

public class RoomTests
{
    private static Room CreateRoom() => new("TEST01");

    // --- Player lifecycle ---

    // Why: the asymmetric-info design depends on join order mapping directly to role
    // (1st = Locksmith, 2nd = Cryptographer, 3rd = Operator) and the first player
    // being the host who can start the game.
    [Fact]
    public void TryAddPlayer_AssignsRolesInOrder()
    {
        var room = CreateRoom();
        var r1 = room.TryAddPlayer("c1", "Alice", out _);
        var r2 = room.TryAddPlayer("c2", "Bob", out _);
        var r3 = room.TryAddPlayer("c3", "Charlie", out _);

        Assert.Equal(PlayerRole.Locksmith, r1);
        Assert.Equal(PlayerRole.Cryptographer, r2);
        Assert.Equal(PlayerRole.Operator, r3);
        Assert.True(room.Players["c1"].IsHost);
    }

    // Why: rooms are capped at 3 players; a 4th join attempt must be rejected cleanly
    // with an error message so the hub can relay it back to the caller.
    [Fact]
    public void TryAddPlayer_RoomFull_ReturnsError()
    {
        var room = CreateRoom();
        room.TryAddPlayer("c1", "Alice", out _);
        room.TryAddPlayer("c2", "Bob", out _);
        room.TryAddPlayer("c3", "Charlie", out _);

        var role = room.TryAddPlayer("c4", "Dave", out var err);

        Assert.Null(role);
        Assert.Equal("Room is full.", err);
    }

    // --- Game progression ---

    // Why: Start() is the gate that kicks off the actual game; we need to confirm
    // it moves the room to Puzzle1, initialises the countdown to the full duration,
    // and stamps StartedAt so duration can be calculated at the end.
    [Fact]
    public void Start_TransitionsToPuzzle1()
    {
        var room = CreateRoom();
        FillRoom(room);
        room.Start();

        Assert.Equal(GameStage.Puzzle1, room.Stage);
        Assert.Equal(Room.GameDurationSeconds, room.SecondsRemaining);
        Assert.NotNull(room.StartedAt);
    }

    // --- Timer ---

    // Why: the loss-by-timer path is one of two ways a game can end; verifying that
    // Tick() correctly transitions to Lost when seconds hit zero confirms the countdown
    // loop and stage mutation work correctly together.
    [Fact]
    public void Tick_TimerReachesZero_GameLost()
    {
        var room = CreateRoom();
        FillAndStart(room);

        room.Tick(Room.GameDurationSeconds * 1000);

        Assert.Equal(GameStage.Lost, room.Stage);
        Assert.Contains("Time's up", room.LastFeedback);
    }

    // --- Puzzle 1 ---

    // Why: solving Puzzle 1 is the only way to reach Puzzle 2; we need to prove that
    // the correct colour-to-digit translation via the cipher unlocks the next stage,
    // confirming the full cooperative puzzle flow works end-to-end.
    [Fact]
    public void SubmitGuess_CorrectAnswer_TransitionsToPuzzle2()
    {
        var room = CreateRoom();
        FillAndStart(room);
        var locksmith = room.Players["c1"];
        var operator_ = room.Players["c3"];

        var correctColors = room.Puzzle1.TargetDigits
            .Select(d => room.Puzzle1.Cipher.First(kv => kv.Value == d).Key)
            .ToList();
        foreach (var c in correctColors)
            room.PressColor(locksmith, c);

        room.SubmitGuess(operator_);

        Assert.Equal(GameStage.Puzzle2, room.Stage);
    }

    // Why: a wrong guess must cost exactly one life and clear the current input so
    // the team can try again — confirming both the penalty and the reset behaviour
    // that keeps Puzzle 1 playable after a mistake.
    [Fact]
    public void SubmitGuess_WrongAnswer_LosesLife()
    {
        var room = CreateRoom();
        FillAndStart(room);
        var locksmith = room.Players["c1"];
        var operator_ = room.Players["c3"];

        var wrongColor = room.Puzzle1.Cipher
            .Where(kv => kv.Value != room.Puzzle1.TargetDigits[0])
            .Select(kv => kv.Key).First();

        for (int i = 0; i < Room.Puzzle1SlotCount; i++)
            room.PressColor(locksmith, wrongColor);

        room.SubmitGuess(operator_);

        Assert.Equal(Room.StartingLives - 1, room.LivesRemaining);
        Assert.Empty(room.Puzzle1.CurrentGuess);
    }

    // --- Puzzle 2 ---

    // Why: Puzzle 2's win condition is time-sensitive; we need to confirm that
    // activating while the needle is inside the safe zone correctly ends the game
    // as Won and timestamps EndedAt for the GameRecord.
    [Fact]
    public void Activate_InSafeZone_Wins()
    {
        var room = CreateRoom();
        FillAndStart(room);
        AdvanceToPuzzle2(room);
        var operator_ = room.Players["c3"];

        for (int i = 0; i < 10000 && !room.Puzzle2.NeedleInSafeZone(); i++)
            room.Puzzle2.Advance(10);

        room.Activate(operator_);

        Assert.Equal(GameStage.Won, room.Stage);
        Assert.NotNull(room.EndedAt);
    }

    // Why: activating outside the safe zone is the penalty path for Puzzle 2;
    // confirming it costs a life (not an instant loss) ensures the game stays fair
    // and the team has a chance to recover.
    [Fact]
    public void Activate_OutsideSafeZone_LosesLife()
    {
        var room = CreateRoom();
        FillAndStart(room);
        AdvanceToPuzzle2(room);
        var operator_ = room.Players["c3"];

        for (int i = 0; i < 10000 && room.Puzzle2.NeedleInSafeZone(); i++)
            room.Puzzle2.Advance(10);

        room.Activate(operator_);

        Assert.Equal(Room.StartingLives - 1, room.LivesRemaining);
    }

    // --- Hint system ---

    // Why: hints are a limited resource; we need to confirm that using one
    // decrements HintsRemaining, increments the total-used counter (written to
    // the GameRecord), and actually surfaces a useful clue in LastHintText.
    [Fact]
    public void RequestHint_DecrementsCountAndSetsHintText()
    {
        var room = CreateRoom();
        FillAndStart(room);
        var cryptographer = room.Players["c2"];

        room.RequestHint(cryptographer);

        Assert.Equal(Room.StartingHints - 1, room.HintsRemaining);
        Assert.Equal(1, room.HintsUsedTotal);
        Assert.NotNull(room.LastHintText);
        Assert.Contains(room.Puzzle1.TargetDigits[0].ToString(), room.LastHintText);
    }

    // Why: once all hints are spent the system must refuse gracefully and not
    // corrupt the usage counter — ensuring the UI can trust HintsRemaining == 0
    // as a reliable "no hints left" signal.
    [Fact]
    public void RequestHint_WhenExhausted_ReturnsNoHintsMessage()
    {
        var room = CreateRoom();
        FillAndStart(room);
        var cryptographer = room.Players["c2"];

        // Use all hints.
        for (int i = 0; i < Room.StartingHints; i++)
            room.RequestHint(cryptographer);

        // One more request beyond the limit.
        room.RequestHint(cryptographer);

        Assert.Equal(0, room.HintsRemaining);
        Assert.Equal(Room.StartingHints, room.HintsUsedTotal); // counter did not increment again
        Assert.Contains("No hints remaining", room.LastHintText);
    }

    // --- Lives-depletion loss condition ---

    // Why: running out of lives (not the timer) is the second way a game ends in a
    // loss; we need to verify the stage flips to Lost, lives reaches exactly 0, and
    // EndedAt is stamped so the GameRecord is written correctly.
    [Fact]
    public void SubmitGuess_DepletingAllLives_TriggersLoss()
    {
        var room = CreateRoom();
        FillAndStart(room);
        var locksmith = room.Players["c1"];
        var operator_ = room.Players["c3"];

        // Build a guaranteed wrong guess (all slots filled with a color whose digit != target).
        var wrongColor = room.Puzzle1.Cipher
            .Where(kv => kv.Value != room.Puzzle1.TargetDigits[0])
            .Select(kv => kv.Key).First();

        for (int life = 0; life < Room.StartingLives; life++)
        {
            for (int slot = 0; slot < Room.Puzzle1SlotCount; slot++)
                room.PressColor(locksmith, wrongColor);
            room.SubmitGuess(operator_);
        }

        Assert.Equal(GameStage.Lost, room.Stage);
        Assert.Equal(0, room.LivesRemaining);
        Assert.Contains("Out of lives", room.LastFeedback);
        Assert.NotNull(room.EndedAt);
    }

    // --- Chat ---

    // Why: chat is bound to 50 messages to keep broadcast payloads small; we need
    // to confirm that the 51st message actually evicts the oldest entry so memory
    // and bandwidth stay bounded during long sessions.
    [Fact]
    public void AddChat_StoresMessageAndTrimsAtLimit()
    {
        var room = CreateRoom();
        FillAndStart(room);
        var alice = room.Players["c1"];

        // Add exactly 51 messages — the 1st should be evicted.
        for (int i = 0; i < 51; i++)
            room.AddChat(alice, $"msg {i}");

        Assert.Equal(50, room.ChatLog.Count);
        Assert.DoesNotContain(room.ChatLog, m => m.Text == "msg 0");
        Assert.Equal("msg 50", room.ChatLog[^1].Text);
    }

    // --- Disconnect during game ---

    // Why: a player crashing mid-game must end the session immediately rather than
    // leaving the other players stuck waiting; verifying RemovePlayer() transitions
    // to Lost confirms the disconnect handler works as a graceful abort.
    [Fact]
    public void RemovePlayer_MidGame_TransitionsToLost()
    {
        var room = CreateRoom();
        FillAndStart(room);

        // Host (Locksmith / c1) disconnects mid-game.
        room.RemovePlayer("c1");

        Assert.Equal(GameStage.Lost, room.Stage);
        Assert.NotNull(room.EndedAt);
        Assert.Contains("left the room", room.LastFeedback);
    }

    // --- Helpers ---

    private static void FillRoom(Room room)
    {
        room.TryAddPlayer("c1", "Alice", out _);
        room.TryAddPlayer("c2", "Bob", out _);
        room.TryAddPlayer("c3", "Charlie", out _);
    }

    private static void FillAndStart(Room room)
    {
        FillRoom(room);
        room.Start();
    }

    private static void AdvanceToPuzzle2(Room room)
    {
        var locksmith = room.Players["c1"];
        var operator_ = room.Players["c3"];
        var correctColors = room.Puzzle1.TargetDigits
            .Select(d => room.Puzzle1.Cipher.First(kv => kv.Value == d).Key).ToList();
        foreach (var c in correctColors)
            room.PressColor(locksmith, c);
        room.SubmitGuess(operator_);
    }
}
