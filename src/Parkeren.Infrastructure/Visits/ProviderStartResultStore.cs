using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStartResultStore(
    ParkerenDbContext dbContext,
    TimeProvider timeProvider) : IProviderStartResultStore
{
    public async Task RecordResponseAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        ArgumentNullException.ThrowIfNull(providerAction);
        var (_, action) = await LoadPersistedAttemptAsync(preparation, cancellationToken);
        action.CaptureStartResponse(providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var (operation, action) = await LoadPersistedAttemptAsync(preparation, cancellationToken);
        action.ResetForRetry();
        operation.ResetForRetry();
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == operation.VisitId, cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        visit.SetHealth(VisitHealth.Healthy);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordConfirmedAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        ArgumentNullException.ThrowIfNull(providerAction);
        var (operation, action) = await LoadPersistedAttemptAsync(preparation, cancellationToken);
        if (operation.Status == ProviderOperationStatus.Unknown)
            operation.BeginReconciliation();
        if (action.Health == ProviderActionHealth.Unknown)
            action.BeginReconciliation();
        action.MarkActive(providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        operation.Succeed(timeProvider.GetUtcNow());
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == operation.VisitId, cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");

        // A Stop request can win while the external Start call is in flight.
        // Reload so an already tracked Starting Visit cannot overwrite that Stop claim.
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        if (visit.Status == VisitStatus.Starting)
        {
            visit.Activate();
            visit.SetHealth(VisitHealth.Healthy);

            var desiredEndAt = ProviderCoverageSchedule.PlanningEndAt(
                visit, action.PlannedEndAt);
            if (desiredEndAt > action.PlannedEndAt)
            {
                var ruleSets = await dbContext.ParkingRuleSets
                    .Include(x => x.PaidWindows)
                    .Include(x => x.CalendarExceptions)
                    .Where(x => x.ValidFrom < desiredEndAt &&
                                (!x.ValidUntil.HasValue || x.ValidUntil.Value > action.PlannedEndAt))
                    .ToListAsync(cancellationToken);
                var nextPaid = ProviderCoverageSchedule.NextPaidSegment(
                    action.PlannedEndAt, desiredEndAt, ruleSets);
                if (nextPaid is not null && !await dbContext.VisitSchedulerWork.AnyAsync(
                    work => work.VisitId == visit.Id &&
                            work.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                            (work.Status == VisitSchedulerWorkStatus.Pending ||
                             work.Status == VisitSchedulerWorkStatus.Claimed),
                    cancellationToken))
                {
                    var dueAt = nextPaid.Start > action.PlannedEndAt
                        ? ProviderCoverageSchedule.PrecheckAt(nextPaid.Start)
                        : ProviderCoverageSchedule.PrecheckAt(action.PlannedEndAt);
                    dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                        Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, dueAt));
                }
            }
        }
        else if (visit.Status != VisitStatus.Stopping)
        {
            throw new InvalidOperationException($"Provider Start cannot be confirmed for Visit in state {visit.Status}.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var (operation, action) = await LoadPersistedAttemptAsync(preparation, cancellationToken);
        if (operation.Status == ProviderOperationStatus.Unknown)
            operation.BeginReconciliation();
        operation.Fail(errorCode, timeProvider.GetUtcNow());
        action.MarkFailed();
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == operation.VisitId, cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        visit.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var (operation, action) = await LoadPersistedAttemptAsync(preparation, cancellationToken);
        if (action.Health != ProviderActionHealth.Unknown)
            action.MarkUnknown();
        if (operation.Status == ProviderOperationStatus.Reconciling)
            operation.ResumeUnknownAfterInterruptedReconciliation();
        if (operation.Status == ProviderOperationStatus.InProgress)
            operation.MarkUnknown(errorCode);
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == operation.VisitId, cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        visit.SetHealth(VisitHealth.Reconciling);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<(ProviderOperation Operation, Parkeren.Domain.Visits.ProviderParkingAction Action)> LoadPersistedAttemptAsync(
        ProviderStartPreparation preparation,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.ProviderOperations.SingleAsync(
            x => x.Id == preparation.Operation.Id && x.VisitId == preparation.Operation.VisitId,
            cancellationToken);
        await dbContext.Entry(operation).ReloadAsync(cancellationToken);
        if (operation.ProviderParkingActionId is not Guid actionId || actionId != preparation.Action.Id)
            throw new InvalidOperationException("Provider start operation does not reference the supplied action.");

        var action = await dbContext.ProviderParkingActions.SingleAsync(
            x => x.Id == actionId && x.VisitId == operation.VisitId,
            cancellationToken);
        await dbContext.Entry(action).ReloadAsync(cancellationToken);
        return (operation, action);
    }
}
