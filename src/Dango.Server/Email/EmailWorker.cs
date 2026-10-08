namespace Dango.Server.Email;

public sealed class EmailWorker(EmailDispatcher dispatcher, ILogger<EmailWorker>? logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await dispatcher.DispatchBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { logger?.LogWarning("Email dispatch interrupted; code=email_dispatch_failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
