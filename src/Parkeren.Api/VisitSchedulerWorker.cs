using Parkeren.Application.Visits;

namespace Parkeren.Api;

internal sealed class VisitSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    FailedSchedulerWorkReleaseQueue failedReleaseQueue,
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
                var terminalRecovery = recoveryScope.ServiceProvider.GetRequiredService<IVisitTerminalRecoveryService>();
                await terminalRecovery.RecoverAsync(stoppingToken);
                logger.LogInformation("Visit startup recovery and terminal work rebuild completed before scheduler claim loop.");
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError("Visit startup recovery failed; retrying before scheduler claims work.");
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
                var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
                if (failedReleaseQueue.Count > 0)
                {
                    try
                    {
                        await failedReleaseQueue.RetryPendingAsync(
                            claimer,
                            timeProvider.GetUtcNow().AddMinutes(1),
                            stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(
                            "Failed to retry pending scheduler work releases; new claims are paused.");
                        await Task.Delay(IdleDelay, timeProvider, stoppingToken);
                        continue;
                    }
                }

                if (timeProvider.GetUtcNow() >= nextProviderCheckAt)
                {
                    nextProviderCheckAt = timeProvider.GetUtcNow() + ProviderCheckInterval;
                    try
                    {
                        var recovery = scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>();
                        await recovery.RecoverExpiredInProgressOperationsAsync(stoppingToken);
                        await recovery.ReconcileUnknownOperationsAsync(stoppingToken);
                        var terminalRecovery = scope.ServiceProvider.GetRequiredService<IVisitTerminalRecoveryService>();
                        await terminalRecovery.RecoverAsync(stoppingToken);
                        await recovery.ReconcileActiveProviderActionsAsync(stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError("Periodic provider action check failed; scheduler work continues.");
                    }
                }
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
            catch (Exception)
            {
                logger.LogError("Visit scheduler worker iteration failed.");
                if (claimedWorkId is Guid workId)
                {
                    try
                    {
                        await using var releaseScope = scopeFactory.CreateAsyncScope();
                        var claimer = releaseScope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
                        await claimer.ReleaseFailedAsync(
                            workId, workerId, timeProvider.GetUtcNow().AddMinutes(1), stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        failedReleaseQueue.Enqueue(workId, workerId);
                        logger.LogError("Failed to release scheduler work {WorkId} after processing error.", workId);
                    }
                }
                await Task.Delay(IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
