namespace EscapeSync.Server.Game;

/// <summary>
/// Authoritative state for the timed-mechanism puzzle.
/// A needle oscillates 0..100 and back; Operator must press ACTIVATE while it's in the safe zone.
/// Locksmith sees the zone bounds; Cryptographer sees the position.
/// </summary>
public class Puzzle2State
{
    private const int SafeZoneWidth = 15;
    private const float NeedleSpeed = 35f; // units per second

    public int SafeZoneStart { get; init; }
    public int SafeZoneEnd { get; init; }

    private float _position;
    private int _direction = 1; // +1 ascending, -1 descending
    public int NeedlePosition => (int)Math.Round(_position);

    public bool NeedleInSafeZone() => NeedlePosition >= SafeZoneStart && NeedlePosition <= SafeZoneEnd;

    public static Puzzle2State Generate(Random? rng = null)
    {
        rng ??= Random.Shared;
        var start = rng.Next(10, 100 - SafeZoneWidth - 10);
        return new Puzzle2State
        {
            SafeZoneStart = start,
            SafeZoneEnd = start + SafeZoneWidth,
            _position = rng.Next(0, 100),
            _direction = rng.Next(2) == 0 ? 1 : -1,
        };
    }

    /// <summary>Moves the needle forward by <paramref name="deltaMilliseconds"/>, bouncing at 0/100.</summary>
    public void Advance(int deltaMilliseconds)
    {
        if (deltaMilliseconds <= 0) return;
        var delta = NeedleSpeed * deltaMilliseconds / 1000f;
        _position += _direction * delta;
        if (_position >= 100f) { _position = 100f; _direction = -1; }
        else if (_position <= 0f) { _position = 0f; _direction = 1; }
    }
}
