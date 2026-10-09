using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Executes an already reserved manual run. Invocation and cross-instance
/// claiming are handled separately by the background dispatcher.
/// </summary>
public sealed class ProviderHistoryReservedRunExecutor(
    ParkerenDbContext db,
    ProviderHistoryCheckpointedImportService importer,
    ProviderHistorySyncRunStore runs,
    TimeProvider clock)
{
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.ProviderHistorySyncRuns.AsNoTracking()
            .SingleAsync(x => x.Id == runId, cancellationToken);
        if (run.Status != ProviderHistorySyncRunStatus.Running)
            throw new InvalidOperationException("Only a running history sync can be executed.");

        try
        {
            await importer.ImportAsync(
                run.ProviderProductId, 10, cancellationToken, run.Mode, run.Id);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown must leave the reserved run Running so another
            // worker can resume from the durable page checkpoint.
            throw;
        }
        catch (Exception error)
        {
            await FinalizeIfRunningAsync(runId, cancelled: false, error.Message);
            throw;
        }
    }

    private async Task FinalizeIfRunningAsync(Guid runId, bool cancelled, string? error)
    {
        // The importer may already have recorded the outcome. Never overwrite it.
        db.ChangeTracker.Clear();
        var status = await db.ProviderHistorySyncRuns.AsNoTracking()
            .Where(x => x.Id == runId)
            .Select(x => (ProviderHistorySyncRunStatus?)x.Status)
            .SingleAsync(CancellationToken.None);
        if (status != ProviderHistorySyncRunStatus.Running)
            return;

        var at = clock.GetUtcNow();
        if (cancelled)
            await runs.CancelAsync(runId, at, CancellationToken.None);
        else
            await runs.FailAsync(runId, at, error, CancellationToken.None);
    }
}
