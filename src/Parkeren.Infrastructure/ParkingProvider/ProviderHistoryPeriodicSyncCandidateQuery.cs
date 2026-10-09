using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Finds products eligible for periodic history synchronization without
/// reserving runs or calling the provider.
/// </summary>
public sealed class ProviderHistoryPeriodicSyncCandidateQuery(
    ParkerenDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<string>> GetDueProductIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - ProviderHistoryPeriodicSyncPolicy.MinimumInterval;

        // Successful bootstrap is a prerequisite. Exclude running imports and
        // enforce a retry interval using the most recent persisted attempt.
        var candidates = await db.ProviderHistorySyncStates.AsNoTracking()
            .Where(state => state.LastSuccessfulSyncAt != null &&
                state.LastSuccessfulSyncAt <= cutoff &&
                (state.LastAttemptAt == null || state.LastAttemptAt <= cutoff) &&
                !db.ProviderHistorySyncRuns.Any(run =>
                    run.ProviderProductId == state.ProviderProductId &&
                    run.Status == ProviderHistorySyncRunStatus.Running))
            .OrderBy(state => state.LastSuccessfulSyncAt)
            .Select(state => state.ProviderProductId)
            .ToListAsync(cancellationToken);

        return candidates;
    }
}
