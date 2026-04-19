namespace EscapeSync.GameLogic;

public sealed class Puzzle3State
{
    public string LeftPair { get; init; } = "00";
    public string RightPair { get; init; } = "00";
    public string TargetCode { get; init; } = "0000";
    public List<int> Entry { get; } = new();

    public static Puzzle3State Generate(Random? rng = null)
    {
        rng ??= Random.Shared;
        var a = rng.Next(0, 100);
        var b = rng.Next(0, 100);
        var left = a.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var right = b.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var target = left + right;
        return new Puzzle3State { LeftPair = left, RightPair = right, TargetCode = target };
    }

    public void PushDigit(int digit)
    {
        if (digit < 0 || digit > 9) return;
        if (Entry.Count >= 4) return;
        Entry.Add(digit);
    }

    public void ClearEntry() => Entry.Clear();

    public bool IsComplete() => Entry.Count == 4;

    public bool MatchesTarget()
    {
        if (!IsComplete()) return false;
        var typed = string.Concat(Entry.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return typed == TargetCode;
    }
}
