using EscapeSync.Shared;

namespace EscapeSync.Server.Game;

/// <summary>
/// Authoritative state for the combination-lock puzzle.
/// Locksmith sees <see cref="CurrentGuess"/>, Cryptographer sees <see cref="Cipher"/> and
/// <see cref="TargetDigits"/>, Operator sees the pips plus a SUBMIT/CLEAR control.
/// </summary>
public class Puzzle1State
{
    /// <summary>Cipher mapping from color to digit. Randomized per game.</summary>
    public IReadOnlyDictionary<LockColor, int> Cipher { get; init; } = new Dictionary<LockColor, int>();

    /// <summary>The target digit sequence that Operator must submit (length = slot count).</summary>
    public IReadOnlyList<int> TargetDigits { get; init; } = Array.Empty<int>();

    /// <summary>The Locksmith's current colored guess (length grows up to slot count).</summary>
    public List<LockColor> CurrentGuess { get; } = new();

    public static Puzzle1State Generate(Random? rng = null)
    {
        rng ??= Random.Shared;

        // Each color gets a distinct digit from 1..9 so the cipher is unambiguous.
        var digits = Enumerable.Range(1, 9).OrderBy(_ => rng.Next()).Take(4).ToArray();
        var colors = Enum.GetValues<LockColor>();
        var cipher = colors.Zip(digits, (c, d) => (c, d)).ToDictionary(x => x.c, x => x.d);

        // Target is a random 4-color sequence (repeats allowed so puzzles feel varied).
        var target = Enumerable.Range(0, Room.Puzzle1SlotCount)
            .Select(_ => colors[rng.Next(colors.Length)])
            .ToArray();
        var targetDigits = target.Select(c => cipher[c]).ToArray();

        return new Puzzle1State { Cipher = cipher, TargetDigits = targetDigits };
    }
}
