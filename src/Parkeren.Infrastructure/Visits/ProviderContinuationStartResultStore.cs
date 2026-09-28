using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderContinuationStartResultStore(ParkerenDbContext dbContext)
    : IProviderContinuationStartResultStore
{
    public async Task RecordResponseAsync(
        ProviderStartPreparation preparation, ProviderAction providerAction,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        preparation.Action.CaptureStartResponse(
            providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordRetryableAsync(
        ProviderStartPreparation preparation, CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        preparation.Action.ResetForRetry();
        preparation.Operation.ResetForRetry();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordConfirmedAsync(
        ProviderStartPreparation preparation, ProviderAction providerAction,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);

        var visit = await LoadVisitAsync(preparation, cancellationToken);
        if (visit.Status is not (VisitStatus.Active or VisitStatus.Stopping))
            throw new InvalidOperationException("Continuation start requires an Active or Stopping Visit.");

        preparation.Action.MarkActive(
            providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        preparation.Operation.Succeed(DateTimeOffset.UtcNow);

        if (visit.Status == VisitStatus.Active)
        {
            visit.SetHealth(VisitHealth.Healthy);
            if (visit.PolicySnapshot.AllowAutoExtension &&
                visit.DesiredEndAt is DateTimeOffset desiredEndAt &&
                desiredEndAt > preparation.Action.PlannedEndAt)
            {
                var nextDueAt = ProviderCoverageSchedule.PrecheckAt(preparation.Action.PlannedEndAt);
                var exists = await dbContext.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id &&
                         x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                         x.DueAt == nextDueAt &&
                         x.Status == VisitSchedulerWorkStatus.Pending,
                    cancellationToken);
                if (!exists)
                    dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                        Guid.NewGuid(), visit.Id,
                        VisitSchedulerWorkType.ContinueProviderCoverage, nextDueAt));
            }
        }
        else
        {
            // Stop may have claimed the Visit while the external Start was in flight.
            // Preserve the claim and flag the newly created action for reconciliation.
            visit.SetHealth(VisitHealth.Reconciling);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordDefinitiveFailureAsync(
        ProviderStartPreparation preparation, string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        preparation.Operation.Fail(errorCode, DateTimeOffset.UtcNow);
        preparation.Action.MarkFailed();
        var visit = await LoadVisitAsync(preparation, cancellationToken);
        visit.SetHealth(VisitHealth.Reconciling);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordUnknownAsync(
        ProviderStartPreparation preparation, string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        preparation.Action.MarkUnknown();
        preparation.Operation.MarkUnknown(errorCode);
        var visit = await LoadVisitAsync(preparation, cancellationToken);
        visit.SetHealth(VisitHealth.Reconciling);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Visit> LoadVisitAsync(
        ProviderStartPreparation preparation, CancellationToken cancellationToken)
    {
        var visit = await dbContext.Visits.SingleAsync(
            x => x.Id == preparation.Operation.VisitId!.Value, cancellationToken);
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        return visit;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> LockVisitAsync(
        ProviderStartPreparation preparation, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        return transaction;
    }

    private static void Validate(ProviderStartPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Operation.Type != ProviderOperationType.ContinueStart)
            throw new InvalidOperationException("Expected a provider continuation start operation.");
    }
}
