using EscapeSync.Shared;

namespace EscapeSync.GameLogic;

public sealed class Puzzle1State
{
    public const int SlotCount = 4;

    public IReadOnlyDictionary<LockColor, int> Cipher { get; init; } = new Dictionary<LockColor, int>();
    public IReadOnlyList<int> TargetDigits { get; init; } = Array.Empty<int>();
    public List<LockColor> CurrentGuess { get; } = new();

    public static Puzzle1State Generate(Random? rng = null)
    {
        rng ??= Random.Shared;
        var digits = Enumerable.Range(1, 9).OrderBy(_ => rng.Next()).Take(4).ToArray();
        var colors = Enum.GetValues<LockColor>();
        var cipher = colors.Zip(digits, (c, d) => (c, d)).ToDictionary(x => x.c, x => x.d);
        var target = Enumerable.Range(0, SlotCount)
            .Select(_ => colors[rng.Next(colors.Length)])
            .ToArray();
        var targetDigits = target.Select(c => cipher[c]).ToArray();
        return new Puzzle1State { Cipher = cipher, TargetDigits = targetDigits };
    }
}
