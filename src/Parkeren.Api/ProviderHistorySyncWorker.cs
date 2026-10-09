using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Api;

/// <summary>
/// Polls durable reservations; a session advisory lock prevents concurrent
/// execution of the same run across API instances.
/// </summary>
internal sealed class ProviderHistorySyncWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<ProviderHistorySyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
                var pending = await db.ProviderHistorySyncRuns.AsNoTracking()
                    .Where(x => x.Status == ProviderHistorySyncRunStatus.Running)
                    .OrderBy(x => x.StartedAt)
                    .Select(x => x.Id)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var runId in pending)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await ProcessClaimedAsync(runId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Provider history worker iteration failed.");
            }

            try { await Task.Delay(IdleDelay, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ProcessClaimedAsync(Guid runId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
        await db.Database.OpenConnectionAsync(stoppingToken);
        var acquired = false;
        try
        {
            acquired = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(hashtextextended({0}, 2)) AS \"Value\"",
                runId.ToString("D")).SingleAsync(stoppingToken);
            if (!acquired) return;

            // Recheck after claiming: a previous worker may have completed the run.
            if (!await db.ProviderHistorySyncRuns.AsNoTracking().AnyAsync(
                x => x.Id == runId && x.Status == ProviderHistorySyncRunStatus.Running,
                stoppingToken))
                return;

            var executor = scope.ServiceProvider.GetRequiredService<ProviderHistoryReservedRunExecutor>();
            await executor.ExecuteAsync(runId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Executor records cancellation; checkpoint remains available for retry.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provider history sync run {RunId} failed.", runId);
        }
        finally
        {
            if (acquired)
                await db.Database.SqlQueryRaw<bool>(
                    "SELECT pg_advisory_unlock(hashtextextended({0}, 2)) AS \"Value\"",
                    runId.ToString("D")).SingleAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
