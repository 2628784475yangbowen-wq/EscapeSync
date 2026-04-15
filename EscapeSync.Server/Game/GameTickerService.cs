namespace EscapeSync.Server.Game;

/// <summary>
/// Background service that drives the countdown timer and Puzzle 2 needle for every live room.
/// Runs roughly 5 times per second.
/// </summary>
public class GameTickerService : BackgroundService
{
    private readonly GameManager _manager;
    private const int TickIntervalMs = 200;

    public GameTickerService(GameManager manager) { _manager = manager; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _manager.TickAllAsync(TickIntervalMs);
            }
            catch (OperationCanceledException) { break; }
            // Tick errors are swallowed to keep the loop alive; GameManager logs internally.
        }
    }
}
