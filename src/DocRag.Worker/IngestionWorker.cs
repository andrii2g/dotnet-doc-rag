namespace DocRag.Worker;

public sealed class IngestionWorker(ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingestion worker is not implemented yet.");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
