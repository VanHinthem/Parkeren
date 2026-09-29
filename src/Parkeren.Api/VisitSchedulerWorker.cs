using Parkeren.Application.Visits;

namespace Parkeren.Api;

internal sealed class VisitSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<VisitSchedulerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProviderCheckInterval = TimeSpan.FromMinutes(1);
    private readonly string workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No scheduled provider mutation is safe until startup reconciliation succeeds.
        // An outage during startup must not terminate the background worker permanently.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var recoveryScope = scopeFactory.CreateAsyncScope();
                var recovery = recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>();
                await recovery.RecoverAsync(stoppingToken);
                logger.LogInformation("Visit startup recovery completed before scheduler claim loop.");
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Visit startup recovery failed; retrying before scheduler claims work.");
                await Task.Delay(IdleDelay, timeProvider, stoppingToken);
            }
        }

        var nextProviderCheckAt = timeProvider.GetUtcNow() + ProviderCheckInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? claimedWorkId = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                if (timeProvider.GetUtcNow() >= nextProviderCheckAt)
                {
                    nextProviderCheckAt = timeProvider.GetUtcNow() + ProviderCheckInterval;
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                            .ReconcileActiveProviderActionsAsync(stoppingToken);
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(exception, "Periodic provider action check failed; scheduler work continues.");
                    }
                }
                var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
                var processor = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>();

                var work = await claimer.ClaimNextDueAsync(workerId, timeProvider.GetUtcNow(), stoppingToken);
                if (work is null)
                {
                    await Task.Delay(IdleDelay, timeProvider, stoppingToken);
                    continue;
                }

                claimedWorkId = work.Id;
                await processor.ProcessAsync(work, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Visit scheduler worker iteration failed.");
                if (claimedWorkId is Guid workId)
                {
                    try
                    {
                        await using var releaseScope = scopeFactory.CreateAsyncScope();
                        var claimer = releaseScope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
                        await claimer.ReleaseFailedAsync(
                            workId, workerId, timeProvider.GetUtcNow().AddMinutes(1), stoppingToken);
                    }
                    catch (Exception releaseException) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(releaseException, "Failed to release scheduler work {WorkId} after processing error.", workId);
                    }
                }
                await Task.Delay(IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
