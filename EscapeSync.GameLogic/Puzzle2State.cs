namespace EscapeSync.GameLogic;

public sealed class Puzzle2State
{
    private const int SafeZoneWidth = 15;
    private const float NeedleSpeed = 35f;

    public int SafeZoneStart { get; init; }
    public int SafeZoneEnd { get; init; }

    private float _position;
    private int _direction = 1;

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

    public void Advance(int deltaMilliseconds)
    {
        if (deltaMilliseconds <= 0) return;
        var delta = NeedleSpeed * deltaMilliseconds / 1000f;
        _position += _direction * delta;
        if (_position >= 100f) { _position = 100f; _direction = -1; }
        else if (_position <= 0f) { _position = 0f; _direction = 1; }
    }
}
