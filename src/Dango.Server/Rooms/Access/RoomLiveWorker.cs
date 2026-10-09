namespace Dango.Server.Rooms.Access;

// Operator opt-in only. The first interval also elapses before any database access.
public sealed class RoomLiveWorker(IServiceScopeFactory scopes, RoomConnectionRegistry registry,
    TimeProvider clock, ILogger<RoomLiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), clock, stoppingToken);
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<RoomLeaseMaintenanceService>()
                    .RunBatchAsync(50, null, stoppingToken, cursor);
                cursor = result.NextCursor;
                var sessions = scope.ServiceProvider.GetRequiredService<Accounts.SessionService>();
                foreach (var connection in registry.All())
                    if (!await sessions.ValidateAsync(connection.UserId, connection.SessionId, stoppingToken))
                        registry.RemoveIfCurrent(connection);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { logger.LogWarning("Room live batch interrupted; code=room_live_failed"); }
        }
    }
}

public sealed class RoomOutboxWorker(RoomOutboxDispatcher dispatcher, TimeProvider clock,
    ILogger<RoomOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RoomOutboxCursor? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), clock, stoppingToken);
                var result = await dispatcher.DispatchBatchAsync(RoomOutboxDispatcher.MaximumBatchSize, null, stoppingToken, cursor);
                // Failed rows also advance this sweep; an empty page begins the next retry cycle.
                cursor = result.NextCursor;
                if (result.Failed > 0) logger.LogWarning("Room notice batch has failures; code=room_notice_failed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { logger.LogWarning("Room notice batch interrupted; code=room_notice_failed"); }
        }
    }
}
