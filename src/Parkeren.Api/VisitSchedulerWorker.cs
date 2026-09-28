using Parkeren.Application.Visits;

namespace Parkeren.Api;

internal sealed class VisitSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<VisitSchedulerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private readonly string workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
                var processor = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>();

                var work = await claimer.ClaimNextDueAsync(workerId, timeProvider.GetUtcNow(), stoppingToken);
                if (work is null)
                {
                    await Task.Delay(IdleDelay, timeProvider, stoppingToken);
                    continue;
                }

                await processor.ProcessAsync(work, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Visit scheduler worker iteration failed.");
                await Task.Delay(IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
