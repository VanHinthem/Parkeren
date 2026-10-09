using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Rechecks eligibility under the existing per-product reservation lock before
/// creating an incremental run. Concurrent schedulers cannot double-reserve.
/// </summary>
public sealed class ProviderHistoryPeriodicSyncRunScheduler(
    ParkerenDbContext db,
    ProviderHistorySyncRunStarter starter,
    TimeProvider clock)
{
    public async Task<int> ReserveDueRunsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - ProviderHistoryPeriodicSyncPolicy.MinimumInterval;
        var candidates = await new ProviderHistoryPeriodicSyncCandidateQuery(db, clock)
            .GetDueProductIdsAsync(cancellationToken);

        var reserved = 0;
        foreach (var productId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A concurrent scheduler may have completed its reservation since
            // candidate selection. Starter serializes reservations by product.
            // Re-read the durable checkpoint to avoid scheduling after recent work.
            var state = await db.ProviderHistorySyncStates.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ProviderProductId == productId, cancellationToken);
            if (state?.LastSuccessfulSyncAt is null ||
                state.LastSuccessfulSyncAt > cutoff ||
                state.LastAttemptAt > cutoff)
                continue;

            if (await starter.TryStartAsync(
                    productId, ProviderHistorySyncRunMode.Incremental, cancellationToken) is not null)
                reserved++;
        }

        return reserved;
    }
}
