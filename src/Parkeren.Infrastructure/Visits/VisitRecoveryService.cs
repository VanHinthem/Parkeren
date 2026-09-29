using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Parkeren.Application.Visits;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitRecoveryService(
    ParkerenDbContext dbContext,
    ContinueVisitProviderReconciler extendReconciler,
    StopVisitProviderReconciler stopReconciler,
    StartVisitProviderReconciler startReconciler,
    IProviderContinuationStartResultStore continuationStartResults,
    IParkingProvider provider,
    Microsoft.Extensions.Logging.ILogger<VisitRecoveryService> logger) : IVisitRecoveryService
{
    public async Task ReconcileActiveProviderActionsAsync(CancellationToken cancellationToken = default)
    {
        var visitIds = await dbContext.Visits.AsNoTracking()
            .Where(x => x.Status == VisitStatus.Active && x.Health == VisitHealth.Healthy)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var visitId in visitIds)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockKey = VisitAdvisoryLock.For(visitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

            var visit = await dbContext.Visits.SingleAsync(x => x.Id == visitId, cancellationToken);
            if (visit.Status != VisitStatus.Active || visit.Health != VisitHealth.Healthy ||
                await dbContext.ProviderOperations.AnyAsync(x => x.VisitId == visitId &&
                    (x.Status == ProviderOperationStatus.Pending ||
                     x.Status == ProviderOperationStatus.InProgress ||
                     x.Status == ProviderOperationStatus.Unknown ||
                     x.Status == ProviderOperationStatus.Reconciling), cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var actions = await dbContext.ProviderParkingActions
                .Where(x => x.VisitId == visitId && x.State == ProviderActionState.Active)
                .ToListAsync(cancellationToken);
            if (actions.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var remoteActions = await provider.GetActionsAsync(cancellationToken);
            foreach (var action in actions)
            {
                var remote = remoteActions.SingleOrDefault(x => x.ProviderActionId == action.ProviderActionId);
                if (remote is not null &&
                    string.Equals(remote.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                    (remote.End - action.PlannedEndAt).Duration() < TimeSpan.FromMilliseconds(1))
                    continue;

                if (remote is not null && string.Equals(remote.Status, "stopped", StringComparison.OrdinalIgnoreCase))
                    action.MarkExternallyStopped(remote.Status);

                visit.SetHealth(VisitHealth.AttentionRequired);
                var work = await dbContext.VisitSchedulerWork
                    .Where(x => x.VisitId == visitId &&
                        (x.Status == VisitSchedulerWorkStatus.Pending ||
                         x.Status == VisitSchedulerWorkStatus.Claimed))
                    .ToListAsync(cancellationToken);
                foreach (var item in work)
                    item.Cancel();
                logger.LogWarning("Provider action {ProviderActionId} for Visit {VisitId} changed externally; continuation blocked.",
                    action.ProviderActionId, visitId);
                break;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await ReleaseClaimedSchedulerWorkAsync(cancellationToken);
        await ReconcileAsync(startup: true, cancellationToken: cancellationToken);
    }

    public Task ReconcileUnknownOperationsAsync(CancellationToken cancellationToken = default) =>
        ReconcileAsync(startup: false, cancellationToken: cancellationToken);

    private async Task ReconcileAsync(bool startup, CancellationToken cancellationToken)
    {
        var items = await LoadAsync(cancellationToken);

        foreach (var item in items)
        {
            if (!startup && !item.UnresolvedOperations.Any(x => x.Status == ProviderOperationStatus.Unknown))
                continue;

            var decision = VisitRecoveryClassifier.Classify(item);

            logger.LogInformation(
                "Visit recovery classified Visit {VisitId} as {RecoveryKind}; status {VisitStatus}; operation {OperationId} ({OperationType}/{OperationStatus}).",
                item.Visit.Id,
                decision.Kind,
                item.Visit.Status,
                decision.Operation?.Id,
                decision.Operation?.Type,
                decision.Operation?.Status);

            if (decision.Kind == VisitRecoveryKind.RebuildScheduler)
            {
                if (startup)
                    await RebuildSchedulerAsync(item, cancellationToken);
                continue;
            }

            if (decision.Kind == VisitRecoveryKind.Ambiguous)
            {
                logger.LogError(
                    "Ambiguous startup recovery for Visit {VisitId}; status {VisitStatus}; unresolved operations {OperationCount}. Automatic provider mutation is blocked and the Visit requires attention.",
                    item.Visit.Id,
                    item.Visit.Status,
                    item.UnresolvedOperations.Count);
                await MarkAmbiguousAsync(item, cancellationToken);
                continue;
            }

            if (decision.Operation is null ||
                decision.Operation.Status != ProviderOperationStatus.Unknown ||
                decision.Operation.ProviderParkingActionId is not Guid actionId)
            {
                logger.LogWarning(
                    "Visit recovery cannot execute {RecoveryKind} for Visit {VisitId}: operation is missing, not Unknown, or has no provider action reference.",
                    decision.Kind,
                    item.Visit.Id);
                continue;
            }

            var action = item.ProviderActions.SingleOrDefault(x => x.Id == actionId);
            if (action is null)
            {
                logger.LogError(
                    "Visit recovery cannot execute {RecoveryKind} for Visit {VisitId}: provider action {ProviderActionId} is missing locally.",
                    decision.Kind,
                    item.Visit.Id,
                    actionId);
                await MarkAmbiguousAsync(item, cancellationToken);
                continue;
            }

            if (decision.Kind is VisitRecoveryKind.ReconcileStart or VisitRecoveryKind.ReconcileContinuationStart)
            {
                var licensePlate = await dbContext.Vehicles
                    .AsNoTracking()
                    .Where(x => x.Id == item.Visit.VehicleId)
                    .Select(x => x.LicensePlate)
                    .SingleOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(licensePlate))
                {
                    logger.LogError(
                        "Visit recovery cannot reconcile Start for Visit {VisitId}: vehicle {VehicleId} has no license plate.",
                        item.Visit.Id,
                        item.Visit.VehicleId);
                    await MarkAmbiguousAsync(item, cancellationToken);
                    continue;
                }

                var preparation = new ProviderStartPreparation(
                    decision.Operation,
                    action,
                    true,
                    false);

                var reconciler = decision.Kind == VisitRecoveryKind.ReconcileContinuationStart
                    ? new StartVisitProviderReconciler(provider, continuationStartResults)
                    : startReconciler;
                var reconciled = await reconciler.ReconcileAsync(preparation, licensePlate, cancellationToken);
                if (reconciled is not null &&
                    decision.Kind == VisitRecoveryKind.ReconcileContinuationStart &&
                    decision.Operation.RequestedEndAt is DateTimeOffset requestedEndAt)
                    await CompletePendingEndTimeChangeAsync(
                        item.Visit.Id, requestedEndAt, cancellationToken);
            }
            else if (decision.Kind == VisitRecoveryKind.ReconcileExtend &&
                decision.Operation.RequestedEndAt is DateTimeOffset requestedEndAt)
            {
                var preparation = new ProviderExtendPreparation(
                    decision.Operation,
                    action,
                    requestedEndAt,
                    true,
                    false);

                await extendReconciler.ReconcileAsync(preparation, cancellationToken);
            }
            else if (decision.Kind == VisitRecoveryKind.ReconcileStop)
            {
                var preparation = new ProviderStopPreparation(
                    decision.Operation,
                    action,
                    true,
                    false);

                await stopReconciler.ReconcileAsync(preparation, cancellationToken);
            }
            else if (decision.Kind == VisitRecoveryKind.ReconcileScheduledCancel)
            {
                var reconciled = await ReconcileScheduledCancelAsync(
                    decision.Operation, action, cancellationToken);
                if (reconciled)
                {
                    var pendingChange = await dbContext.VisitEndTimeChanges.AsNoTracking()
                        .Where(x => x.VisitId == item.Visit.Id &&
                                    x.Result == VisitEndTimeChangeResult.Pending)
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (pendingChange?.RequestedDesiredEndAt is DateTimeOffset pendingRequestedEndAt)
                        await CompletePendingEndTimeChangeAsync(
                            item.Visit.Id, pendingRequestedEndAt, cancellationToken);
                }
            }

            await ReevaluateAfterReconciliationAsync(item.Visit.Id, cancellationToken);
        }
    }



    private async Task<bool> ReconcileScheduledCancelAsync(
        ProviderOperation operation,
        Parkeren.Domain.Visits.ProviderParkingAction action,
        CancellationToken cancellationToken)
    {
        if (operation.Status != ProviderOperationStatus.Unknown ||
            action.State != ProviderActionState.Stopping ||
            action.Health != ProviderActionHealth.Unknown ||
            string.IsNullOrWhiteSpace(action.ProviderActionId))
            throw new InvalidOperationException("Only an unknown scheduled-action cancellation can be reconciled.");

        var remoteActions = await provider.GetActionsAsync(cancellationToken);
        var remote = remoteActions.SingleOrDefault(x => x.ProviderActionId == action.ProviderActionId);
        if (remote is null || !string.Equals(remote.Status, "stopped", StringComparison.OrdinalIgnoreCase))
            return false;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var persistedOperation = await dbContext.ProviderOperations
            .SingleAsync(x => x.Id == operation.Id, cancellationToken);
        var persistedAction = await dbContext.ProviderParkingActions
            .SingleAsync(x => x.Id == action.Id, cancellationToken);

        persistedOperation.BeginReconciliation();
        persistedAction.BeginReconciliation();
        persistedAction.MarkStopped(DateTimeOffset.UtcNow, remote.Status);
        persistedOperation.Succeed(DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }


    private async Task CompletePendingEndTimeChangeAsync(
        Guid visitId,
        DateTimeOffset requestedEndAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var change = await dbContext.VisitEndTimeChanges
            .Where(x => x.VisitId == visitId &&
                        x.Result == VisitEndTimeChangeResult.Pending &&
                        x.RequestedDesiredEndAt == requestedEndAt)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (change is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == visitId, cancellationToken);
        visit.ChangeDesiredEndAt(requestedEndAt);
        change.MarkApplied();

        var obsoleteWork = await dbContext.VisitSchedulerWork
            .Where(x => x.VisitId == visitId &&
                        x.Status == VisitSchedulerWorkStatus.Pending &&
                        x.DueAt >= requestedEndAt)
            .ToListAsync(cancellationToken);
        foreach (var work in obsoleteWork)
            work.Cancel();

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReevaluateAfterReconciliationAsync(
        Guid visitId,
        CancellationToken cancellationToken)
    {
        var refreshedItems = await LoadAsync(cancellationToken);
        var refreshedItem = refreshedItems.SingleOrDefault(x => x.Visit.Id == visitId);
        if (refreshedItem is null)
            return;

        var refreshedDecision = VisitRecoveryClassifier.Classify(refreshedItem);
        if (refreshedDecision.Kind == VisitRecoveryKind.RebuildScheduler)
        {
            await RebuildSchedulerAsync(refreshedItem, cancellationToken);
            return;
        }

        if (refreshedDecision.Kind == VisitRecoveryKind.Ambiguous)
        {
            logger.LogError(
                "Visit {VisitId} remained ambiguous after startup reconciliation. Automatic provider mutation is blocked and the Visit requires attention.",
                visitId);
            await MarkAmbiguousAsync(refreshedItem, cancellationToken);
        }
    }

    private async Task MarkAmbiguousAsync(
        VisitRecoveryItem item,
        CancellationToken cancellationToken)
    {
        var visit = await dbContext.Visits
            .SingleAsync(x => x.Id == item.Visit.Id, cancellationToken);

        visit.SetHealth(VisitHealth.AttentionRequired);

        var schedulerWork = await dbContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id &&
                        (x.Status == VisitSchedulerWorkStatus.Pending ||
                         x.Status == VisitSchedulerWorkStatus.Claimed))
            .ToListAsync(cancellationToken);

        foreach (var work in schedulerWork)
            work.Cancel();

        await dbContext.SaveChangesAsync(cancellationToken);
    }


    private async Task ReleaseClaimedSchedulerWorkAsync(CancellationToken cancellationToken)
    {
        var claimedWork = await dbContext.VisitSchedulerWork
            .Where(x => x.Status == VisitSchedulerWorkStatus.Claimed)
            .ToListAsync(cancellationToken);

        foreach (var work in claimedWork)
        {
            var dueAt = work.DueAt;
            if (work.ClaimedAt is DateTimeOffset claimedAt && dueAt <= claimedAt)
                dueAt = claimedAt.AddTicks(1);

            work.Release(dueAt);
        }

        if (claimedWork.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RebuildSchedulerAsync(
        VisitRecoveryItem item,
        CancellationToken cancellationToken)
    {
        if (item.Visit.Status != VisitStatus.Active ||
            item.Visit.DesiredEndAt is not DateTimeOffset desiredEndAt)
            return;

        var activeAction = item.ProviderActions
            .Where(x => x.State == ProviderActionState.Active)
            .OrderByDescending(x => x.PlannedEndAt)
            .FirstOrDefault();

        if (activeAction is null || string.IsNullOrWhiteSpace(activeAction.ProviderActionId))
        {
            if (activeAction is null &&
                item.ProviderActions.All(x => x.State == ProviderActionState.Completed) &&
                await RebuildFreeStartWorkAsync(
                    item.Visit, desiredEndAt,
                    item.ProviderActions.Count == 0
                        ? item.Visit.StartAt
                        : item.ProviderActions.Max(x => x.PlannedEndAt),
                    cancellationToken))
                return;

            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        var providerActions = await provider.GetActionsAsync(cancellationToken);
        var confirmedAction = providerActions.SingleOrDefault(x =>
            x.ProviderActionId == activeAction.ProviderActionId);

        if (confirmedAction is null)
        {
            logger.LogError(
                "Visit recovery cannot rebuild scheduler for Visit {VisitId}: provider action {ProviderActionId} was not confirmed by the provider.",
                item.Visit.Id,
                activeAction.ProviderActionId);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        if (string.Equals(confirmedAction.Status, "stopped", StringComparison.OrdinalIgnoreCase))
        {
            var persistedAction = await dbContext.ProviderParkingActions
                .SingleAsync(x => x.Id == activeAction.Id, cancellationToken);
            persistedAction.MarkExternallyStopped(confirmedAction.Status);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Provider action {ProviderActionId} for Visit {VisitId} was stopped outside Parkeren; continuation is blocked pending review.",
                activeAction.ProviderActionId, item.Visit.Id);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        if (!string.Equals(confirmedAction.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Provider action {ProviderActionId} for Visit {VisitId} has unexpected status {ProviderStatus}; continuation is blocked.",
                activeAction.ProviderActionId, item.Visit.Id, confirmedAction.Status);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        if ((confirmedAction.End - activeAction.PlannedEndAt).Duration() >= TimeSpan.FromMilliseconds(1))
        {
            logger.LogWarning(
                "Provider action {ProviderActionId} for Visit {VisitId} has end {ProviderEndAt}, while the locally confirmed end is {LocalEndAt}; continuation is blocked pending review.",
                activeAction.ProviderActionId, item.Visit.Id, confirmedAction.End, activeAction.PlannedEndAt);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        if (desiredEndAt <= confirmedAction.End)
            return;

        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < desiredEndAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > confirmedAction.End))
            .ToListAsync(cancellationToken);
        var nextPaid = ProviderCoverageSchedule.NextPaidSegment(confirmedAction.End, desiredEndAt, ruleSets);
        if (nextPaid is null)
            return;

        var dueAt = nextPaid.Start > confirmedAction.End
            ? nextPaid.Start
            : ProviderCoverageSchedule.PrecheckAt(confirmedAction.End);
        var exists = await dbContext.VisitSchedulerWork.AnyAsync(
            x => x.VisitId == item.Visit.Id &&
                 x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                 (x.Status == VisitSchedulerWorkStatus.Pending ||
                  x.Status == VisitSchedulerWorkStatus.Claimed),
            cancellationToken);

        if (exists)
            return;

        dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
            Guid.NewGuid(),
            item.Visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            dueAt));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> RebuildFreeStartWorkAsync(
        Visit visit,
        DateTimeOffset endAt,
        DateTimeOffset fromAt,
        CancellationToken cancellationToken)
    {
        var startAt = visit.StartAt;
        if (endAt <= startAt)
            return false;

        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < endAt && (!x.ValidUntil.HasValue || x.ValidUntil.Value > startAt))
            .ToListAsync(cancellationToken);

        try
        {
            var paid = fromAt < endAt
                ? ProviderCoverageSchedule.NextPaidSegment(fromAt, endAt, ruleSets)
                : null;
            if (paid is null)
                return true;

            var exists = await dbContext.VisitSchedulerWork.AnyAsync(
                x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                     (x.Status == VisitSchedulerWorkStatus.Pending || x.Status == VisitSchedulerWorkStatus.Claimed),
                cancellationToken);
            if (exists)
                return true;

            if (paid.Start <= DateTimeOffset.UtcNow)
                return false;

            dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, paid.Start));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<VisitRecoveryItem>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var visits = await dbContext.Visits
            .AsNoTracking()
            .Where(x => x.Status == VisitStatus.Starting ||
                        x.Status == VisitStatus.Active ||
                        x.Status == VisitStatus.Stopping)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        if (visits.Count == 0)
            return [];

        var visitIds = visits.Select(x => x.Id).ToArray();

        var actions = await dbContext.ProviderParkingActions
            .AsNoTracking()
            .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
            .ToListAsync(cancellationToken);

        var operations = await dbContext.ProviderOperations
            .AsNoTracking()
            .Where(x => x.VisitId.HasValue &&
                        visitIds.Contains(x.VisitId.Value) &&
                        (x.Status == ProviderOperationStatus.Pending ||
                         x.Status == ProviderOperationStatus.InProgress ||
                         x.Status == ProviderOperationStatus.Unknown ||
                         x.Status == ProviderOperationStatus.Reconciling))
            .ToListAsync(cancellationToken);

        return visits.Select(visit => new VisitRecoveryItem(
            visit,
            actions.Where(x => x.VisitId == visit.Id).ToArray(),
            operations.Where(x => x.VisitId == visit.Id).ToArray()))
            .ToArray();
    }
}
