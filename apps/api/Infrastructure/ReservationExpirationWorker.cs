using DealerOS.Modules.Reservations.Application;

namespace DealerOS.Api.Infrastructure;

public sealed class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Reservations:ExpirationWorkerEnabled", true)) return;
        var intervalSeconds = Math.Max(10, configuration.GetValue("Reservations:ExpirationWorkerIntervalSeconds", 60));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ReservationService>();
                var count = await service.ExpireDueAsync(stoppingToken);
                logger.LogInformation("Reservation expiration cycle completed with {ExpiredCount} expirations.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Reservation expiration cycle failed; the next cycle will retry.");
            }
        }
    }
}
