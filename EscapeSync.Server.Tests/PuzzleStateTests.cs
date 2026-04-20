using EscapeSync.Server.Game;
using EscapeSync.Shared;

namespace EscapeSync.Server.Tests;

public class Puzzle1StateTests
{
    // Why: the cipher must cover every LockColor exactly once with a unique digit
    // in 1-9; if any colour is missing or digits repeat, the Cryptographer's view
    // would be meaningless and the puzzle unsolvable.
    [Fact]
    public void Generate_CipherCoversAllColorsWithUniqueDigits()
    {
        var state = Puzzle1State.Generate();

        foreach (var color in Enum.GetValues<LockColor>())
            Assert.True(state.Cipher.ContainsKey(color));

        Assert.Equal(state.Cipher.Values.Count(), state.Cipher.Values.Distinct().Count());

        foreach (var digit in state.Cipher.Values)
            Assert.InRange(digit, 1, 9);
    }

    // Why: the target sequence must be resolvable through the cipher — if any
    // target digit isn't present as a cipher value the puzzle becomes impossible
    // and no valid colour sequence would ever open the lock.
    [Fact]
    public void Generate_TargetDigitsMatchCipher()
    {
        var state = Puzzle1State.Generate();

        Assert.Equal(Room.Puzzle1SlotCount, state.TargetDigits.Count);
        foreach (var digit in state.TargetDigits)
            Assert.True(state.Cipher.Values.Contains(digit));
    }
}

public class Puzzle2StateTests
{
    // Why: the safe zone must fit within the 0-100 scale and be exactly 15 units
    // wide; an out-of-bounds or wrongly-sized window would make the puzzle
    // impossible or trivially easy.
    [Fact]
    public void Generate_SafeZoneIsValid()
    {
        var state = Puzzle2State.Generate();

        Assert.True(state.SafeZoneStart >= 0);
        Assert.True(state.SafeZoneEnd <= 100);
        Assert.Equal(15, state.SafeZoneEnd - state.SafeZoneStart);
    }

    // Why: the needle must bounce inside [0, 100] indefinitely; if it ever
    // escaped that range the Cryptographer would see an invalid position and
    // the win-condition check would break.
    [Fact]
    public void Advance_NeedleBounces_StaysInRange()
    {
        var state = Puzzle2State.Generate(new Random(42));

        for (int i = 0; i < 1000; i++)
        {
            state.Advance(100);
            Assert.InRange(state.NeedlePosition, 0, 100);
        }
    }
}

