using Microsoft.Extensions.DependencyInjection;

namespace Dango.Server.Rooms.Management;

// Registration is deliberately opt-in at the formal host boundary.
public sealed class RoomExpiryWorker(IServiceScopeFactory scopes, ILogger<RoomExpiryWorker>? logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RoomExpiryCursor? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<RoomExpiryService>()
                    .PurgeBatchAsync(RoomExpiryPolicy.MaximumBatchSize, cursor, null, stoppingToken);
                // Advance past failed targets too; an empty page starts a later retry sweep.
                cursor = result.NextCursor;
                if (result.Failed > 0) logger?.LogWarning("Room expiry batch has failed targets; code=room_expiry_failed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { logger?.LogWarning("Room expiry batch interrupted; code=room_expiry_failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
