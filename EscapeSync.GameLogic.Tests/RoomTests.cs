using EscapeSync.GameLogic;
using EscapeSync.Shared;

using Xunit;

namespace EscapeSync.GameLogic.Tests;

public sealed class RoomTests
{
    private static void AddThreePlayers(Room r)
    {
        Assert.NotNull(r.TryAddPlayer("a", "Amy", out _));
        Assert.NotNull(r.TryAddPlayer("b", "Ben", out _));
        Assert.NotNull(r.TryAddPlayer("c", "Cal", out _));
    }

    [Fact]
    public void TryAddPlayer_rejects_duplicate_nickname()
    {
        var r = new Room("ZZZZZZ");
        Assert.NotNull(r.TryAddPlayer("a", "Same", out _));
        Assert.Null(r.TryAddPlayer("b", "same", out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void TryAddPlayer_rejects_when_full()
    {
        var r = new Room("YYYYYY");
        AddThreePlayers(r);
        Assert.Null(r.TryAddPlayer("d", "Dan", out var err));
        Assert.Contains("full", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_throws_when_not_full()
    {
        var r = new Room("XXXXXX");
        Assert.NotNull(r.TryAddPlayer("a", "A", out _));
        Assert.Throws<InvalidOperationException>(() => r.Start());
    }

    [Fact]
    public void Timer_decrements_across_subsecond_ticks()
    {
        var r = new Room("NNNNNN");
        AddThreePlayers(r);
        r.Start(new Random(1));
        var start = r.SecondsRemaining;
        for (var i = 0; i < 5; i++)
            r.Tick(200);
        Assert.Equal(start - 1, r.SecondsRemaining);
    }

    [Fact]
    public void Timer_expiration_marks_lost()
    {
        var r = new Room("WWWWWW");
        AddThreePlayers(r);
        r.Start(new Random(1));
        r.Tick((Room.GameDurationSeconds + 5) * 1000);
        Assert.Equal(GameStage.Lost, r.Stage);
    }

    [Fact]
    public void Wrong_lock_submission_costs_a_life()
    {
        var r = new Room("VVVVVV");
        AddThreePlayers(r);
        r.Start(new Random(4));
        var locksmith = r.Players.Values.First(p => p.Role == PlayerRole.Locksmith);
        var op = r.Players.Values.First(p => p.Role == PlayerRole.Operator);
        var wrong = r.Puzzle1.TargetDigits.ToArray();
        wrong[0] = wrong[0] >= 9 ? 1 : wrong[0] + 1;
        foreach (var d in wrong)
        {
            var color = r.Puzzle1.Cipher.First(kv => kv.Value == d).Key;
            r.PressColor(locksmith, color);
        }
        r.SubmitGuess(op);
        Assert.Equal(Room.StartingLives - 1, r.LivesRemaining);
        Assert.Equal(GameStage.Puzzle1, r.Stage);
    }

    [Fact]
    public void Hint_reduces_pool_and_sets_text()
    {
        var r = new Room("UUUUUU");
        AddThreePlayers(r);
        r.Start(new Random(6));
        var any = r.Players.Values.First();
        r.RequestHint(any);
        Assert.Equal(Room.StartingHints - 1, r.HintsRemaining);
        Assert.False(string.IsNullOrEmpty(r.LastHintText));
    }

    [Fact]
    public void Non_operator_cannot_submit_door_digits()
    {
        var r = new Room("TTTTTT");
        AddThreePlayers(r);
        r.Start(new Random(1));
        AdvanceToPuzzle3(r);
        var locksmith = r.Players.Values.First(p => p.Role == PlayerRole.Locksmith);
        var before = r.Puzzle3.Entry.Count;
        r.PushDoorDigit(locksmith, 1);
        Assert.Equal(before, r.Puzzle3.Entry.Count);
    }

    [Fact]
    public void SubmitDoorEntry_ignored_until_four_digits()
    {
        var r = new Room("SSSSSS");
        AddThreePlayers(r);
        r.Start(new Random(2));
        AdvanceToPuzzle3(r);
        var op = r.Players.Values.First(p => p.Role == PlayerRole.Operator);
        r.PushDoorDigit(op, 0);
        r.SubmitDoorEntry(op);
        Assert.Equal(GameStage.Puzzle3, r.Stage);
    }

    [Fact]
    public void Wrong_door_code_costs_life_and_clears_entry()
    {
        var r = new Room("RRRRRR");
        AddThreePlayers(r);
        r.Start(new Random(3));
        AdvanceToPuzzle3(r);
        var op = r.Players.Values.First(p => p.Role == PlayerRole.Operator);
        for (var i = 0; i < 4; i++)
            r.PushDoorDigit(op, 0);
        r.SubmitDoorEntry(op);
        Assert.Equal(Room.StartingLives - 1, r.LivesRemaining);
        Assert.Empty(r.Puzzle3.Entry);
        Assert.Equal(GameStage.Puzzle3, r.Stage);
    }

    [Fact]
    public void Full_escape_path_reaches_won()
    {
        var r = new Room("QQQQQQ");
        AddThreePlayers(r);
        r.Start(new Random(77));
        var locksmith = r.Players.Values.First(p => p.Role == PlayerRole.Locksmith);
        var op = r.Players.Values.First(p => p.Role == PlayerRole.Operator);
        foreach (var digit in r.Puzzle1.TargetDigits)
        {
            var color = r.Puzzle1.Cipher.First(kv => kv.Value == digit).Key;
            r.PressColor(locksmith, color);
        }
        r.SubmitGuess(op);
        Assert.Equal(GameStage.Puzzle2, r.Stage);
        var n = 0;
        while (n++ < 60000 && r.Stage == GameStage.Puzzle2 && !r.Puzzle2.NeedleInSafeZone())
            r.Tick(15);
        Assert.True(r.Puzzle2.NeedleInSafeZone());
        r.Activate(op);
        Assert.Equal(GameStage.Puzzle3, r.Stage);
        foreach (var ch in r.Puzzle3.TargetCode)
            r.PushDoorDigit(op, ch - '0');
        r.SubmitDoorEntry(op);
        Assert.Equal(GameStage.Won, r.Stage);
    }

    [Fact]
    public void RemovePlayer_during_puzzle_aborts_session()
    {
        var r = new Room("PPPPPP");
        AddThreePlayers(r);
        r.Start(new Random(5));
        r.RemovePlayer("a");
        Assert.Equal(GameStage.Lost, r.Stage);
    }

    [Fact]
    public void AddChat_trims_and_caps_length()
    {
        var r = new Room("OOOOOO");
        AddThreePlayers(r);
        r.Start(new Random(1));
        var p = r.Players.Values.First();
        var longText = new string('x', 400);
        r.AddChat(p, longText);
        Assert.True(r.ChatLog[^1].Text.Length <= 280);
    }

    private static void AdvanceToPuzzle3(Room r)
    {
        var locksmith = r.Players.Values.First(p => p.Role == PlayerRole.Locksmith);
        var op = r.Players.Values.First(p => p.Role == PlayerRole.Operator);
        foreach (var digit in r.Puzzle1.TargetDigits)
        {
            var color = r.Puzzle1.Cipher.First(kv => kv.Value == digit).Key;
            r.PressColor(locksmith, color);
        }
        r.SubmitGuess(op);
        var n = 0;
        while (n++ < 60000 && r.Stage == GameStage.Puzzle2 && !r.Puzzle2.NeedleInSafeZone())
            r.Tick(15);
        r.Activate(op);
        Assert.Equal(GameStage.Puzzle3, r.Stage);
    }
}
