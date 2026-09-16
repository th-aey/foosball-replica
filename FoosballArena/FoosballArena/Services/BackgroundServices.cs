namespace FoosballArena.Services;

/// <summary>Drives every room's state machine, 5 times per second.</summary>
public class GameLoopService : BackgroundService
{
    private readonly RoomManager _rooms;
    public GameLoopService(RoomManager rooms) => _rooms = rooms;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                _rooms.TickAll();
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }
}

/// <summary>Evicts participants whose heartbeat stopped (closed tab, crash, dead Wi-Fi).</summary>
public class RoomCleanupService : BackgroundService
{
    private readonly RoomManager _rooms;
    public RoomCleanupService(RoomManager rooms) => _rooms = rooms;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                _rooms.SweepStale();
        }
        catch (OperationCanceledException) { }
    }
}
