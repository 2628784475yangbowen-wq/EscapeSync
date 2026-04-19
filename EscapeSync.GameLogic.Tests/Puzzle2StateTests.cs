using EscapeSync.GameLogic;

using Xunit;

namespace EscapeSync.GameLogic.Tests;

public sealed class Puzzle2StateTests
{
    [Fact]
    public void Advance_keeps_needle_within_zero_and_hundred()
    {
        var p = Puzzle2State.Generate(new Random(321));
        for (var i = 0; i < 8000; i++)
        {
            p.Advance(12);
            Assert.InRange(p.NeedlePosition, 0, 100);
        }
    }

    [Fact]
    public void Needle_eventually_enters_generated_safe_zone()
    {
        var p = Puzzle2State.Generate(new Random(44));
        var n = 0;
        while (n++ < 50000 && !p.NeedleInSafeZone())
            p.Advance(8);
        Assert.True(p.NeedleInSafeZone());
        Assert.InRange(p.NeedlePosition, p.SafeZoneStart, p.SafeZoneEnd);
    }
}
