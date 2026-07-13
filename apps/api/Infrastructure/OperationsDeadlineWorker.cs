using DealerOS.Modules.Operations.Application;

namespace DealerOS.Api.Infrastructure;

public sealed class OperationsDeadlineWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OperationsDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Operations:DeadlineWorkerEnabled", true)) return;

        var intervalMinutes = Math.Max(1, configuration.GetValue("Operations:DeadlineWorkerIntervalMinutes", 15));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IOperationsStore>();
                var now = timeProvider.GetUtcNow();
                var created = 0;
                foreach (var execution in await store.ListActiveAsync(stoppingToken))
                    created += execution.GenerateDeadlineNotifications(now, TimeSpan.FromDays(1));
                if (created > 0) await store.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Generated {NotificationCount} operations deadline notifications.", created);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Operations deadline notification cycle failed; the next cycle will retry.");
            }
        }
    }
}
