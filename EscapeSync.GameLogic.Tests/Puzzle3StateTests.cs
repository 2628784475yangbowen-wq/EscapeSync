using EscapeSync.GameLogic;

using Xunit;

namespace EscapeSync.GameLogic.Tests;

public sealed class Puzzle3StateTests
{
    [Fact]
    public void PushDigit_respects_four_slot_cap()
    {
        var p = new Puzzle3State { LeftPair = "12", RightPair = "34", TargetCode = "1234" };
        for (var i = 0; i < 10; i++)
            p.PushDigit(1);
        Assert.Equal(4, p.Entry.Count);
    }

    [Fact]
    public void MatchesTarget_requires_exact_four_digits()
    {
        var p = new Puzzle3State { LeftPair = "01", RightPair = "99", TargetCode = "0199" };
        p.PushDigit(0);
        p.PushDigit(1);
        p.PushDigit(9);
        Assert.False(p.MatchesTarget());
        p.PushDigit(9);
        Assert.True(p.MatchesTarget());
    }

    [Fact]
    public void ClearEntry_empties_buffer()
    {
        var p = Puzzle3State.Generate(new Random(7));
        p.PushDigit(5);
        p.ClearEntry();
        Assert.Empty(p.Entry);
    }

    [Fact]
    public void Generate_produces_four_char_target()
    {
        var p = Puzzle3State.Generate(new Random(3));
        Assert.Equal(2, p.LeftPair.Length);
        Assert.Equal(2, p.RightPair.Length);
        Assert.Equal(4, p.TargetCode.Length);
        Assert.Equal(p.LeftPair + p.RightPair, p.TargetCode);
    }
}
