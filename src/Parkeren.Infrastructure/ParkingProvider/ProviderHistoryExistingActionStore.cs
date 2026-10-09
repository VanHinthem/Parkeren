using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using Parkeren.Infrastructure.Visits;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class ProviderHistoryExistingActionStore(ParkerenDbContext dbContext)
    : IProviderHistoryExistingActionStore
{
    public async Task<ProviderHistoryExistingActionResult> ApplyIfExistingAsync(
        string providerProductId,
        ProviderActionHistoryRecord record,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProviderActionId))
            throw new ArgumentException("Provider action id is required.", nameof(record));

        var providerActionId = record.ProviderActionId.Trim();
        var existing = await dbContext.ProviderParkingActions
            .SingleOrDefaultAsync(x => x.ProviderActionId == providerActionId, cancellationToken);
        if (existing is null)
            return ProviderHistoryExistingActionResult.NotFound;

        // Existing index is globally unique. Never reinterpret the same provider action
        // ID as belonging to a different product, even if it is returned by that product.
        if (!string.Equals(existing.ProviderProductId, providerProductId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Provider action {providerActionId} belongs to a different product.");

        if (existing.Origin == ProviderActionOrigin.Managed)
        {
            // Never alter an in-flight Visit, even if a historical read reports COMPLETED.
            if (existing.VisitId is not Guid visitId ||
                existing.State != ProviderActionState.Completed ||
                existing.Health != ProviderActionHealth.Healthy ||
                existing.HistoryStatus is not (ProviderHistoryStatus.Reconciled or ProviderHistoryStatus.Incomplete) ||
                !string.Equals(record.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                return ProviderHistoryExistingActionResult.SkippedManaged;

            // Page import owns the transaction: share the Visit lock with VisitRecovery.
            if (dbContext.Database.CurrentTransaction is null)
                return ProviderHistoryExistingActionResult.SkippedManaged;

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({VisitAdvisoryLock.For(visitId)})", cancellationToken);

            // The action may have changed while waiting for VisitRecovery's lock.
            await dbContext.Entry(existing).ReloadAsync(cancellationToken);
            var completedVisit = await dbContext.Visits.AsNoTracking()
                .AnyAsync(x => x.Id == visitId &&
                               x.Status == VisitStatus.Completed &&
                               x.Health == VisitHealth.Healthy, cancellationToken);
            var inFlight = await dbContext.ProviderOperations.AsNoTracking()
                .AnyAsync(x => x.VisitId == visitId &&
                    (x.Status == ProviderOperationStatus.Pending ||
                     x.Status == ProviderOperationStatus.InProgress ||
                     x.Status == ProviderOperationStatus.Unknown ||
                     x.Status == ProviderOperationStatus.Reconciling), cancellationToken);
            var pendingHistory = await dbContext.VisitSchedulerWork.AsNoTracking()
                .AnyAsync(x => x.ProviderParkingActionId == existing.Id &&
                    x.Type == VisitSchedulerWorkType.ReconcileProviderAction &&
                    (x.Status == VisitSchedulerWorkStatus.Pending ||
                     x.Status == VisitSchedulerWorkStatus.Claimed), cancellationToken);
            if (!completedVisit || inFlight || pendingHistory ||
                existing.Origin != ProviderActionOrigin.Managed ||
                existing.VisitId != visitId ||
                existing.State != ProviderActionState.Completed ||
                existing.Health != ProviderActionHealth.Healthy ||
                existing.HistoryStatus is not (ProviderHistoryStatus.Reconciled or ProviderHistoryStatus.Incomplete))
                return ProviderHistoryExistingActionResult.SkippedManaged;

            existing.RefreshFinalizedManagedHistory(
                record.ActualStartAt, record.ActualEndAt,
                record.ProviderCostAmount, record.Status, observedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ProviderHistoryExistingActionResult.RefreshedManaged;
        }

        // Only finalized provider history can revise imported facts. An active or
        // unexpected status must not turn a completed imported action into a
        // misleading reconciliation result.
        if (!string.Equals(record.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only completed history actions can refresh imported actions.");

        existing.RefreshImportedHistory(
            record.ActualStartAt,
            record.ActualEndAt,
            record.ProviderCostAmount,
            record.Status,
            observedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProviderHistoryExistingActionResult.RefreshedImported;
    }
}
