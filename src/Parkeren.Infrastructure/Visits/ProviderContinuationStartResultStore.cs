using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderContinuationStartResultStore(
    ParkerenDbContext dbContext,
    TimeProvider timeProvider,
    NotificationInboxWriter inboxWriter)
    : IProviderContinuationStartResultStore
{
    public async Task RecordResponseAsync(
        ProviderStartPreparation preparation, ProviderAction providerAction,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        var (_, action) = await LoadOperationAsync(preparation, cancellationToken);
        action.CaptureStartResponse(
            providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordRetryableAsync(
        ProviderStartPreparation preparation, CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        var (operation, action) = await LoadOperationAsync(preparation, cancellationToken);
        action.ResetForRetry();
        operation.ResetForRetry();
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
        var (operation, action) = await LoadOperationAsync(preparation, cancellationToken);

        var visit = await LoadVisitAsync(preparation, cancellationToken);
        if (visit.Status is not (VisitStatus.Active or VisitStatus.Stopping))
            throw new InvalidOperationException("Continuation start requires an Active or Stopping Visit.");

        if (operation.Status == ProviderOperationStatus.Unknown &&
            preparation.Operation.Status == ProviderOperationStatus.Reconciling)
            operation.BeginReconciliation();
        if (action.Health == ProviderActionHealth.Unknown &&
            preparation.Action.Health == ProviderActionHealth.Reconciling)
            action.BeginReconciliation();

        if (string.Equals(providerAction.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            action.MarkScheduled(providerAction.ProviderActionId, providerAction.Status);
        else
            action.MarkActive(
                providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        operation.Succeed(timeProvider.GetUtcNow());

        if (visit.Status == VisitStatus.Active)
        {
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
                var nextPaid = ProviderCoverageSchedule.NextPaidSegment(action.PlannedEndAt, desiredEndAt, ruleSets);
                if (nextPaid is null)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
                var nextDueAt = nextPaid.Start > action.PlannedEndAt
                    ? nextPaid.Start
                    : ProviderCoverageSchedule.PrecheckAt(action.PlannedEndAt);
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

        var successEvent = new NotificationEvent(
            Guid.NewGuid(),
            NotificationEventType.ProviderContinuationSucceeded,
            visit.Id,
            timeProvider.GetUtcNow());
        dbContext.NotificationEvents.Add(successEvent);
        await inboxWriter.WriteAsync(successEvent, NotificationType.ProviderContinuationSucceeded, visit.UserId, includeVisitor: false, includeAdmins: true, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordDefinitiveFailureAsync(
        ProviderStartPreparation preparation, string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        var (operation, action) = await LoadOperationAsync(preparation, cancellationToken);
        operation.Fail(errorCode, timeProvider.GetUtcNow());
        action.MarkFailed();
        var visit = await LoadVisitAsync(preparation, cancellationToken);
        visit.SetHealth(VisitHealth.Reconciling);
        var notificationEvent = new NotificationEvent(
            Guid.NewGuid(),
            NotificationEventType.ProviderContinuationAttentionRequired,
            visit.Id,
            timeProvider.GetUtcNow());
        dbContext.NotificationEvents.Add(notificationEvent);
        await inboxWriter.WriteAsync(notificationEvent, NotificationType.ProviderContinuationAttentionRequired, visit.UserId, includeVisitor: true, includeAdmins: true, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordUnknownAsync(
        ProviderStartPreparation preparation, string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        Validate(preparation);
        await using var transaction = await LockVisitAsync(preparation, cancellationToken);
        var (operation, action) = await LoadOperationAsync(preparation, cancellationToken);
        action.MarkUnknown();
        operation.MarkUnknown(errorCode);
        var visit = await LoadVisitAsync(preparation, cancellationToken);
        visit.SetHealth(VisitHealth.Reconciling);
        var notificationEvent = new NotificationEvent(
            Guid.NewGuid(),
            NotificationEventType.ProviderContinuationAttentionRequired,
            visit.Id,
            timeProvider.GetUtcNow());
        dbContext.NotificationEvents.Add(notificationEvent);
        await inboxWriter.WriteAsync(notificationEvent, NotificationType.ProviderContinuationAttentionRequired, visit.UserId, includeVisitor: true, includeAdmins: true, cancellationToken);
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

    private async Task<(ProviderOperation Operation, Parkeren.Domain.Visits.ProviderParkingAction Action)> LoadOperationAsync(
        ProviderStartPreparation preparation, CancellationToken cancellationToken)
    {
        var operation = await dbContext.ProviderOperations.SingleAsync(
            x => x.Id == preparation.Operation.Id, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(
            x => x.Id == preparation.Action.Id, cancellationToken);
        if (operation.ProviderParkingActionId != action.Id ||
            operation.VisitId != action.VisitId ||
            operation.Type != ProviderOperationType.ContinueStart)
            throw new InvalidOperationException("Continuation operation and action do not match.");
        return (operation, action);
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
