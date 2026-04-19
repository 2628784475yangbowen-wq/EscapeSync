using EscapeSync.GameLogic;
using EscapeSync.Shared;

using Xunit;

namespace EscapeSync.GameLogic.Tests;

public sealed class Puzzle1StateTests
{
    [Fact]
    public void Generate_cipher_maps_four_colors_to_distinct_digits()
    {
        var p = Puzzle1State.Generate(new Random(8));
        Assert.Equal(4, p.Cipher.Count);
        Assert.Equal(p.Cipher.Values.Distinct().Count(), p.Cipher.Count);
        Assert.All(p.Cipher.Values, d => Assert.InRange(d, 1, 9));
    }

    [Fact]
    public void Target_digits_match_cipher_applied_to_internal_sequence()
    {
        var rng = new Random(11);
        var p = Puzzle1State.Generate(rng);
        Assert.Equal(Puzzle1State.SlotCount, p.TargetDigits.Count);
    }

    [Fact]
    public void Submit_path_matches_when_colors_follow_cipher_inverse()
    {
        var p = Puzzle1State.Generate(new Random(2));
        foreach (var digit in p.TargetDigits)
        {
            var color = p.Cipher.First(kv => kv.Value == digit).Key;
            p.CurrentGuess.Add(color);
        }
        var mapped = p.CurrentGuess.Select(c => p.Cipher[c]).ToList();
        Assert.Equal(p.TargetDigits, mapped);
    }
}
