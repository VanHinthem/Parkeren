using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Parkeren.Application.Visits;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Notifications;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitRecoveryService(
    ParkerenDbContext dbContext,
    ContinueVisitProviderReconciler extendReconciler,
    StopVisitProviderReconciler stopReconciler,
    StartVisitProviderReconciler startReconciler,
    IProviderStartStore providerStartStore,
    StartVisitProviderExecutor startProviderExecutor,
    IProviderContinuationStartResultStore continuationStartResults,
    IVisitEndTimeProviderAdjuster endTimeProviderAdjuster,
    IVisitEndTimeChanger endTimeChanger,
    IParkingProvider provider,
    IProviderDiscrepancyService discrepancyService,
    NotificationInboxWriter inboxWriter,
    IProviderOperationExecutionTracker executionTracker,
    TimeProvider timeProvider,
    StopVisitFlow stopVisitFlow,
    Microsoft.Extensions.Logging.ILogger<VisitRecoveryService> logger) : IVisitRecoveryService
{
    public async Task ReconcileActiveProviderActionsAsync(CancellationToken cancellationToken = default)
    {
        await RecoverProviderActionHistoryWorkAsync(cancellationToken);

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

            var remoteActions = string.IsNullOrWhiteSpace(visit.ProviderProductExternalId)
                ? await provider.GetActionsAsync(cancellationToken)
                : await provider.GetActionsForProductAsync(visit.ProviderProductExternalId, cancellationToken);

            foreach (var action in actions)
            {
                var remote = ProviderActionMatchPolicy.FindUniqueMatch(
                    remoteActions,
                    new ProviderActionMatchCriteria(
                        action.ProviderActionId,
                        action.ProviderProductId));
                var productId = visit.ProviderProductId;
                var mismatch = false;

                if (productId is Guid localProductId)
                {
                    var missingKey = MissingProviderActionKey(localProductId, action.Id);
                    var statusKey = ProviderActionStatusKey(localProductId, action.Id);
                    var endKey = ProviderActionEndKey(localProductId, action.Id);
                    var observedAt = timeProvider.GetUtcNow();

                    if (remote is null)
                    {
                        await discrepancyService.ObserveAsync(
                            new ProviderDiscrepancyObservation(
                                missingKey,
                                ProviderDiscrepancyType.MissingProviderAction,
                                localProductId,
                                observedAt,
                                visit.Id,
                                action.Id,
                                action.ProviderActionId),
                            cancellationToken);
                        await discrepancyService.ResolveAsync(statusKey, observedAt, cancellationToken);
                        await discrepancyService.ResolveAsync(endKey, observedAt, cancellationToken);
                        mismatch = true;
                    }
                    else if (!string.Equals(remote.Status, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        await discrepancyService.ResolveAsync(missingKey, observedAt, cancellationToken);
                        await discrepancyService.ResolveAsync(endKey, observedAt, cancellationToken);
                        await discrepancyService.ObserveAsync(
                            new ProviderDiscrepancyObservation(
                                statusKey,
                                ProviderDiscrepancyType.ProviderActionStatusMismatch,
                                localProductId,
                                observedAt,
                                visit.Id,
                                action.Id,
                                remote.ProviderActionId,
                                remote.Status,
                                remote.Start,
                                remote.End),
                            cancellationToken);
                        mismatch = true;

                        if (string.Equals(remote.Status, "stopped", StringComparison.OrdinalIgnoreCase))
                            await discrepancyService.ResolveAsync(statusKey, observedAt, cancellationToken);
                    }
                    else
                    {
                        await discrepancyService.ResolveAsync(missingKey, observedAt, cancellationToken);
                        await discrepancyService.ResolveAsync(statusKey, observedAt, cancellationToken);

                        if (!ProviderActionMatchPolicy.TimestampsMatch(remote.End, action.PlannedEndAt))
                        {
                            await discrepancyService.ObserveAsync(
                                new ProviderDiscrepancyObservation(
                                    endKey,
                                    ProviderDiscrepancyType.ProviderActionEndMismatch,
                                    localProductId,
                                    observedAt,
                                    visit.Id,
                                    action.Id,
                                    remote.ProviderActionId,
                                    remote.Status,
                                    remote.Start,
                                    remote.End),
                                cancellationToken);
                            mismatch = true;
                        }
                        else
                        {
                            await discrepancyService.ResolveAsync(endKey, observedAt, cancellationToken);
                        }
                    }
                }
                else
                {
                    mismatch = remote is null ||
                               !string.Equals(remote.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                               !ProviderActionMatchPolicy.TimestampsMatch(remote.End, action.PlannedEndAt);
                }

                if (remote is not null &&
                    string.Equals(remote.Status, "stopped", StringComparison.OrdinalIgnoreCase) &&
                    action.State == ProviderActionState.Active)
                {
                    action.MarkExternallyStopped(remote.Status);
                    await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
                        dbContext, action, timeProvider.GetUtcNow().AddMinutes(1), cancellationToken);
                }

                if (!mismatch)
                    continue;

                var attentionReason = remote is not null &&
                    string.Equals(remote.Status, "stopped", StringComparison.OrdinalIgnoreCase)
                    ? "ExternalStop"
                    : null;
                await MarkAttentionRequiredAsync(visit, cancellationToken, attentionReason);
                var work = await dbContext.VisitSchedulerWork
                    .Where(x => x.VisitId == visitId &&
                        x.Type != VisitSchedulerWorkType.ReconcileProviderAction &&
                        (x.Status == VisitSchedulerWorkStatus.Pending ||
                         x.Status == VisitSchedulerWorkStatus.Claimed))
                    .ToListAsync(cancellationToken);
                foreach (var item in work)
                    item.Cancel();

                logger.LogWarning(
                    "Provider action {ProviderActionId} for Visit {VisitId} changed externally; continuation blocked.",
                    action.ProviderActionId,
                    visitId);
                break;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await DetectExternalProviderActionsAsync(cancellationToken);
    }

    private async Task DetectExternalProviderActionsAsync(CancellationToken cancellationToken)
    {
        var products = await dbContext.ParkingProviderProducts.AsNoTracking()
            .Where(x => x.IsAvailable)
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            var hasUnresolvedOperations = await (
                from operation in dbContext.ProviderOperations.AsNoTracking()
                join visit in dbContext.Visits.AsNoTracking()
                    on operation.VisitId equals visit.Id
                where visit.ProviderProductId == product.Id &&
                      (operation.Status == ProviderOperationStatus.Pending ||
                       operation.Status == ProviderOperationStatus.InProgress ||
                       operation.Status == ProviderOperationStatus.Unknown ||
                       operation.Status == ProviderOperationStatus.Reconciling)
                select operation.Id)
                .AnyAsync(cancellationToken);

            if (hasUnresolvedOperations)
                continue;

            var remoteActions = await provider.GetActionsForProductAsync(
                product.ProviderProductId,
                cancellationToken);
            var localProviderActionIds = await dbContext.ProviderParkingActions.AsNoTracking()
                .Where(x => x.ProviderProductId == product.ProviderProductId &&
                            x.ProviderActionId != null)
                .Select(x => x.ProviderActionId!)
                .ToListAsync(cancellationToken);
            var localIds = localProviderActionIds.ToHashSet(StringComparer.Ordinal);

            foreach (var localProviderActionId in localIds)
            {
                await discrepancyService.ResolveAsync(
                    ExternalProviderActionKey(product.Id, localProviderActionId),
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            }

            foreach (var remote in remoteActions.Where(x => !localIds.Contains(x.ProviderActionId)))
            {
                var observedAt = timeProvider.GetUtcNow();
                var key = ExternalProviderActionKey(product.Id, remote.ProviderActionId);

                await discrepancyService.ObserveAsync(
                    new ProviderDiscrepancyObservation(
                        key,
                        ProviderDiscrepancyType.ExternalProviderAction,
                        product.Id,
                        observedAt,
                        ProviderActionId: remote.ProviderActionId,
                        ProviderStatus: remote.Status,
                        ProviderStartAt: remote.Start,
                        ProviderEndAt: remote.End),
                    cancellationToken);

                if (string.Equals(remote.Status, "stopped", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(remote.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    await discrepancyService.ResolveAsync(key, observedAt, cancellationToken);
                }
            }
        }
    }

    private static string MissingProviderActionKey(Guid productId, Guid actionId) =>
        $"missing-provider-action:{productId:N}:{actionId:N}";

    private static string ProviderActionStatusKey(Guid productId, Guid actionId) =>
        $"provider-action-status:{productId:N}:{actionId:N}";

    private static string ProviderActionEndKey(Guid productId, Guid actionId) =>
        $"provider-action-end:{productId:N}:{actionId:N}";

    private static string ExternalProviderActionKey(Guid productId, string providerActionId) =>
        $"external-provider-action:{productId:N}:{providerActionId}";

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await ReleaseClaimedSchedulerWorkAsync(cancellationToken);
        await RecoverProviderActionHistoryWorkAsync(cancellationToken);
        await MarkStaleInProgressProviderOperationsUnknownAsync(cancellationToken);
        await ResumeInterruptedProviderReconciliationsAsync(cancellationToken);
        await ReconcileAsync(startup: true, cancellationToken: cancellationToken);
    }

    private async Task RecoverProviderActionHistoryWorkAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => (x.State == ProviderActionState.Stopped || x.State == ProviderActionState.Completed) &&
                        x.VisitId.HasValue &&
                        !string.IsNullOrWhiteSpace(x.ProviderActionId) &&
                        (x.HistoryStatus == ProviderHistoryStatus.NotRequired ||
                         x.HistoryStatus == ProviderHistoryStatus.Pending))
            .Select(x => new { x.Id, VisitId = x.VisitId!.Value })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockKey = VisitAdvisoryLock.For(candidate.VisitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

            var action = await dbContext.ProviderParkingActions
                .SingleOrDefaultAsync(x => x.Id == candidate.Id, cancellationToken);
            if (action is not null)
            {
                if (action.HistoryStatus == ProviderHistoryStatus.NotRequired)
                    await ProviderActionInitialCostInitializer.TryInitializeAsync(
                        dbContext, action, cancellationToken);
                await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
                    dbContext, action, timeProvider.GetUtcNow(), cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    public Task RecoverExpiredInProgressOperationsAsync(CancellationToken cancellationToken = default) =>
        MarkStaleInProgressProviderOperationsUnknownAsync(cancellationToken);

    private async Task MarkStaleInProgressProviderOperationsUnknownAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var staleBefore = now.Subtract(ProviderOperationStartupRecovery.AttemptLease);
        var candidates = await dbContext.ProviderOperations
            .AsNoTracking()
            .Where(x => x.Status == ProviderOperationStatus.InProgress &&
                        x.VisitId.HasValue &&
                        (!x.AttemptStartedAt.HasValue || x.AttemptStartedAt <= staleBefore))
            .Select(x => new { x.Id, VisitId = x.VisitId!.Value })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockKey = VisitAdvisoryLock.For(candidate.VisitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

            var operation = await dbContext.ProviderOperations
                .SingleOrDefaultAsync(x => x.Id == candidate.Id, cancellationToken);
            if (operation is null || operation.Status != ProviderOperationStatus.InProgress)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            using var recoveryClaim = executionTracker.TryClaimRecovery(operation.OperationId);
            if (recoveryClaim is null ||
                (operation.AttemptStartedAt.HasValue && operation.AttemptStartedAt > staleBefore))
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            if (operation.ProviderParkingActionId is not Guid actionId)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var action = await dbContext.ProviderParkingActions
                .SingleOrDefaultAsync(x => x.Id == actionId, cancellationToken);
            if (action is null)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            if (!ProviderOperationStartupRecovery.MarkStaleInProgressUnknown(operation, action, now))
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var visit = await dbContext.Visits.SingleAsync(x => x.Id == candidate.VisitId, cancellationToken);
            visit.SetHealth(VisitHealth.Reconciling);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task ResumeInterruptedProviderReconciliationsAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.ProviderOperations
            .AsNoTracking()
            .Where(x => x.Status == ProviderOperationStatus.Reconciling && x.VisitId.HasValue)
            .Select(x => new { x.Id, VisitId = x.VisitId!.Value })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockKey = VisitAdvisoryLock.For(candidate.VisitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

            var operation = await dbContext.ProviderOperations
                .SingleOrDefaultAsync(x => x.Id == candidate.Id, cancellationToken);
            if (operation is null || operation.Status != ProviderOperationStatus.Reconciling ||
                operation.ProviderParkingActionId is not Guid actionId)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var action = await dbContext.ProviderParkingActions
                .SingleOrDefaultAsync(x => x.Id == actionId, cancellationToken);
            if (action is null)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            if (ProviderOperationStartupRecovery.ResumeInterruptedReconciliation(operation, action))
            {
                var visit = await dbContext.Visits.SingleAsync(x => x.Id == candidate.VisitId, cancellationToken);
                visit.SetHealth(VisitHealth.Reconciling);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    public Task ReconcileUnknownOperationsAsync(CancellationToken cancellationToken = default) =>
        ReconcileAsync(startup: false, cancellationToken: cancellationToken);

    private async Task ReconcileAsync(bool startup, CancellationToken cancellationToken)
    {
        await RecoverPendingEndTimeChangesAsync(cancellationToken);
        var resumedStops = await ResumeStoppingVisitsAsync(cancellationToken);
        var items = await LoadAsync(cancellationToken);

        foreach (var item in items)
        {
            if (resumedStops.Contains(item.Visit.Id))
                continue;

            var hasUnknownOperation = item.UnresolvedOperations
                .Any(x => x.Status == ProviderOperationStatus.Unknown);
            if (!hasUnknownOperation)
            {
                var retryableStart = item.UnresolvedOperations.SingleOrDefault(x =>
                    x.Type == ProviderOperationType.Start &&
                    x.Status == ProviderOperationStatus.Pending);
                var retryableAction = retryableStart is null
                    ? null
                    : item.ProviderActions.SingleOrDefault(x =>
                        x.Id == retryableStart.ProviderParkingActionId &&
                        x.State == ProviderActionState.Planned &&
                        x.Health == ProviderActionHealth.Healthy);

                if (item.Visit.Status == VisitStatus.Starting &&
                    retryableStart is not null && retryableAction is not null)
                {
                    var licensePlate = await GetRecoveryLicensePlateAsync(item.Visit, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(licensePlate) &&
                        await RetryConfirmedAbsentStartAsync(
                            item.Visit, retryableStart, retryableAction, licensePlate, cancellationToken))
                        await ReevaluateAfterReconciliationAsync(item.Visit.Id, cancellationToken);
                    continue;
                }

                if (!startup)
                    continue;
            }

            if (!startup && !hasUnknownOperation)
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
                var licensePlate = await GetRecoveryLicensePlateAsync(item.Visit, cancellationToken);

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
                if (reconciled is null && decision.Kind == VisitRecoveryKind.ReconcileStart)
                    await RetryConfirmedAbsentStartAsync(
                        item.Visit, decision.Operation, action, licensePlate, cancellationToken);

                if (reconciled is not null &&
                    decision.Kind == VisitRecoveryKind.ReconcileContinuationStart &&
                    decision.Operation.ParentOperationId is Guid parentOperationId)
                    await CompletePendingEndTimeChangeAsync(
                        item.Visit.Id, parentOperationId, cancellationToken);
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
                if (reconciled && decision.Operation.ParentOperationId is Guid parentOperationId)
                    await CompletePendingEndTimeChangeAsync(
                        item.Visit.Id, parentOperationId, cancellationToken);
            }

            await ReevaluateAfterReconciliationAsync(item.Visit.Id, cancellationToken);
        }
    }

    private async Task<HashSet<Guid>> ResumeStoppingVisitsAsync(CancellationToken cancellationToken)
    {
        var visits = await dbContext.Visits.AsNoTracking()
            .Where(x => x.Status == VisitStatus.Stopping)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);
        var resumed = new HashSet<Guid>();

        foreach (var visit in visits)
        {
            var stopOperation = await dbContext.ProviderOperations.AsNoTracking()
                .Where(x => x.VisitId == visit.Id &&
                            x.Type == ProviderOperationType.Stop &&
                            x.Status != ProviderOperationStatus.Failed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (stopOperation is null)
                continue;

            var hasUnresolvedNonExtendMutation = await dbContext.ProviderOperations.AsNoTracking()
                .AnyAsync(x => x.VisitId == visit.Id &&
                               x.Type != ProviderOperationType.Stop &&
                               x.Type != ProviderOperationType.Extend &&
                               (x.Status == ProviderOperationStatus.Pending ||
                                x.Status == ProviderOperationStatus.InProgress ||
                                x.Status == ProviderOperationStatus.Unknown ||
                                x.Status == ProviderOperationStatus.Reconciling),
                    cancellationToken);
            if (hasUnresolvedNonExtendMutation)
                continue;

            resumed.Add(visit.Id);
            await stopVisitFlow.ResumePersistedStopAsync(
                new StopVisitClaim(visit, stopOperation, IsReplay: true, IsAlreadyCompleted: false),
                cancellationToken);
        }

        return resumed;
    }

    private async Task<string?> GetRecoveryLicensePlateAsync(
        Visit visit,
        CancellationToken cancellationToken) =>
        await dbContext.Vehicles
            .AsNoTracking()
            .Where(x => x.Id == visit.VehicleId)
            .Select(x => x.LicensePlate)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> RetryConfirmedAbsentStartAsync(
        Visit visit,
        ProviderOperation operation,
        Parkeren.Domain.Visits.ProviderParkingAction action,
        string licensePlate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(visit.ProviderLocation))
        {
            logger.LogError(
                "Cannot retry provider Start for Visit {VisitId}: provider location is missing.",
                visit.Id);
            return false;
        }

        var pendingOperation = await dbContext.ProviderOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == operation.Id, cancellationToken);
        var plannedAction = await dbContext.ProviderParkingActions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == action.Id, cancellationToken);
        if (pendingOperation?.Status != ProviderOperationStatus.Pending ||
            plannedAction?.State != ProviderActionState.Planned ||
            plannedAction.Health != ProviderActionHealth.Healthy)
            return false;

        ProviderStartPreparation preparation;
        try
        {
            preparation = await providerStartStore.PrepareAttemptAsync(
                new StartVisitClaimResult(visit, true, true),
                plannedAction.PlannedEndAt,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogInformation(
                exception,
                "Provider Start retry for Visit {VisitId} was not claimed because its persisted state changed.",
                visit.Id);
            return false;
        }

        var execution = await startProviderExecutor.ExecuteAsync(
            preparation,
            new ProviderStartRequest(
                licensePlate,
                visit.ProviderLocation,
                preparation.Action.PlannedEndAt,
                visit.ProviderProductExternalId),
            cancellationToken);

        if (execution.RequiresReconciliation)
            logger.LogInformation("Provider Start retry for Visit {VisitId} requires another reconciliation.", visit.Id);

        return !execution.RequiresReconciliation && !execution.DefinitiveFailure;
    }



    private async Task RecoverPendingEndTimeChangesAsync(CancellationToken cancellationToken)
    {
        var pendingChanges = await dbContext.VisitEndTimeChanges.AsNoTracking()
            .Where(x => x.Result == VisitEndTimeChangeResult.Pending &&
                        x.RequestedDesiredEndAt.HasValue)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var change in pendingChanges)
        {
            var children = await dbContext.ProviderOperations.AsNoTracking()
                .Where(x => x.ParentOperationId == change.OperationId)
                .ToListAsync(cancellationToken);

            if (children.Any(x => x.Status is ProviderOperationStatus.Pending
                                 or ProviderOperationStatus.InProgress
                                 or ProviderOperationStatus.Unknown
                                 or ProviderOperationStatus.Reconciling))
                continue;

            var successfulCancel = children
                .Where(x => x.Type == ProviderOperationType.Stop &&
                            x.Status == ProviderOperationStatus.Succeeded &&
                            x.ProviderParkingActionId.HasValue)
                .OrderByDescending(x => x.CompletedAt)
                .FirstOrDefault();

            if (successfulCancel is null)
                continue;

            var providerParkingActionId = successfulCancel.ProviderParkingActionId;
            if (providerParkingActionId is not Guid originalActionId)
                continue;

            var originalAction = await dbContext.ProviderParkingActions.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == originalActionId,
                    cancellationToken);
            if (originalAction is null ||
                change.RequestedDesiredEndAt is not DateTimeOffset requestedEndAt)
                continue;

            var needsReplacement =
                originalAction.PlannedStartAt < requestedEndAt &&
                originalAction.PlannedEndAt > requestedEndAt;

            if (!needsReplacement)
            {
                await CompletePendingEndTimeChangeAsync(
                    change.VisitId, change.OperationId, cancellationToken);
                continue;
            }

            var hasReplacement = children.Any(x =>
                x.Type == ProviderOperationType.ContinueStart &&
                x.RequestedEndAt == requestedEndAt);

            if (!hasReplacement)
            {
                var visit = await dbContext.Visits.AsNoTracking()
                    .SingleAsync(x => x.Id == change.VisitId, cancellationToken);
                var command = new ChangeVisitEndTimeCommand(
                    change.OperationId,
                    change.VisitId,
                    change.ActorUserId,
                    requestedEndAt);

                var adjustment = await endTimeProviderAdjuster.AdjustAsync(
                    command, cancellationToken);
                if (adjustment.RequiresReconciliation)
                    continue;
            }

            await endTimeChanger.ApplyAsync(
                new ChangeVisitEndTimeCommand(
                    change.OperationId,
                    change.VisitId,
                    change.ActorUserId,
                    requestedEndAt),
                cancellationToken);
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

        var remoteActions = string.IsNullOrWhiteSpace(action.ProviderProductId)
            ? await provider.GetActionsAsync(cancellationToken)
            : await provider.GetActionsForProductAsync(action.ProviderProductId, cancellationToken);
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
        var stoppedAt = timeProvider.GetUtcNow();
        persistedAction.MarkStopped(stoppedAt, remote.Status, remote.Start);
        await ProviderActionInitialCostInitializer.TryInitializeAsync(dbContext, persistedAction, cancellationToken);
        await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
            dbContext, persistedAction, stoppedAt.AddMinutes(1), cancellationToken);
        persistedOperation.Succeed(stoppedAt);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }


    private async Task CompletePendingEndTimeChangeAsync(
        Guid visitId,
        Guid parentOperationId,
        CancellationToken cancellationToken)
    {
        var change = await dbContext.VisitEndTimeChanges.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.VisitId == visitId &&
                     x.OperationId == parentOperationId &&
                     x.Result == VisitEndTimeChangeResult.Pending,
                cancellationToken);
        if (change is null)
            return;

        await endTimeChanger.ApplyAsync(
            new ChangeVisitEndTimeCommand(
                change.OperationId,
                change.VisitId,
                change.ActorUserId,
                change.RequestedDesiredEndAt),
            cancellationToken);
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

        await MarkAttentionRequiredAsync(visit, cancellationToken);

        var schedulerWork = await dbContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id &&
                        x.Type != VisitSchedulerWorkType.ReconcileProviderAction &&
                        (x.Status == VisitSchedulerWorkStatus.Pending ||
                         x.Status == VisitSchedulerWorkStatus.Claimed))
            .ToListAsync(cancellationToken);

        foreach (var work in schedulerWork)
            work.Cancel();

        await dbContext.SaveChangesAsync(cancellationToken);
    }


    private async Task MarkAttentionRequiredAsync(
        Visit visit,
        CancellationToken cancellationToken,
        string? reason = null)
    {
        var becameAttentionRequired = visit.Health != VisitHealth.AttentionRequired;
        visit.SetHealth(VisitHealth.AttentionRequired);

        if (!becameAttentionRequired)
            return;

        var alreadyNotified = await dbContext.NotificationEvents
            .AnyAsync(
                x => x.Type == NotificationEventType.ProviderContinuationAttentionRequired &&
                     x.AggregateId == visit.Id,
                cancellationToken);
        if (alreadyNotified)
            return;

        var occurredAt = timeProvider.GetUtcNow();
        var notificationEvent = new NotificationEvent(
            Guid.NewGuid(),
            NotificationEventType.ProviderContinuationAttentionRequired,
            visit.Id,
            occurredAt);

        dbContext.NotificationEvents.Add(notificationEvent);
        var payload = reason is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(new { Reason = reason });

        await inboxWriter.WriteAsync(
            notificationEvent,
            NotificationType.ProviderContinuationAttentionRequired,
            visit.UserId,
            includeVisitor: true,
            includeAdmins: true,
            cancellationToken,
            payload,
            visit.Id);
    }

    private async Task ReleaseClaimedSchedulerWorkAsync(CancellationToken cancellationToken)
    {
        var claimedWork = await dbContext.VisitSchedulerWork
            .AsNoTracking()
            .Where(x => x.Status == VisitSchedulerWorkStatus.Claimed)
            .Select(x => new { x.Id, x.VisitId })
            .ToListAsync(cancellationToken);

        foreach (var candidate in claimedWork)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var lockKey = VisitAdvisoryLock.For(candidate.VisitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

            var work = await dbContext.VisitSchedulerWork
                .FromSqlInterpolated($"SELECT w.*, w.xmin FROM visit_scheduler_work AS w WHERE w.\"Id\" = {candidate.Id} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (work is null || work.Status != VisitSchedulerWorkStatus.Claimed)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var visit = await dbContext.Visits.AsNoTracking()
                .SingleAsync(x => x.Id == work.VisitId, cancellationToken);
            var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(
                work.Type, visit.Status, visit.Health);
            var claimedAt = work.ClaimedAt
                ?? throw new InvalidOperationException("Claimed scheduler work has no claim time.");

            switch (decision)
            {
                case VisitSchedulerWorkExecutionDecision.Execute:
                {
                    var dueAt = work.DueAt > claimedAt
                        ? work.DueAt
                        : claimedAt.AddTicks(1);
                    work.Release(dueAt);
                    break;
                }

                case VisitSchedulerWorkExecutionDecision.Defer:
                {
                    var dueAt = timeProvider.GetUtcNow()
                        .Add(VisitSchedulerWorkExecutionPolicy.DefaultDeferDelay);
                    work.Release(dueAt > claimedAt ? dueAt : claimedAt.AddTicks(1));
                    break;
                }

                case VisitSchedulerWorkExecutionDecision.Cancel:
                    work.Cancel();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(decision), decision, "Unsupported scheduler work execution decision.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task RebuildSchedulerAsync(
        VisitRecoveryItem item,
        CancellationToken cancellationToken)
    {
        if (item.Visit.Status != VisitStatus.Active ||
            item.Visit.DesiredEndAt is not DateTimeOffset desiredEndAt)
            return;

        var candidateAction = item.ProviderActions
            .Where(x => x.State is ProviderActionState.Active or ProviderActionState.Scheduled)
            .OrderByDescending(x => x.PlannedEndAt)
            .FirstOrDefault();

        if (candidateAction is null || string.IsNullOrWhiteSpace(candidateAction.ProviderActionId))
        {
            if (candidateAction is null &&
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

        var providerActions = string.IsNullOrWhiteSpace(candidateAction.ProviderProductId)
            ? await provider.GetActionsAsync(cancellationToken)
            : await provider.GetActionsForProductAsync(candidateAction.ProviderProductId, cancellationToken);
        var confirmedAction = ProviderActionMatchPolicy.FindUniqueMatch(
            providerActions,
            new ProviderActionMatchCriteria(
                candidateAction.ProviderActionId,
                candidateAction.ProviderProductId));

        if (confirmedAction is null)
        {
            logger.LogError(
                "Visit recovery cannot rebuild scheduler for Visit {VisitId}: provider action {ProviderActionId} was not confirmed by the provider.",
                item.Visit.Id,
                candidateAction.ProviderActionId);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        var isScheduledAction = candidateAction.State == ProviderActionState.Scheduled;
        if (string.Equals(confirmedAction.Status, "stopped", StringComparison.OrdinalIgnoreCase))
        {
            if (!isScheduledAction)
            {
                var persistedAction = await dbContext.ProviderParkingActions
                    .SingleAsync(x => x.Id == candidateAction.Id, cancellationToken);
                persistedAction.MarkExternallyStopped(confirmedAction.Status);
                await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
                    dbContext, persistedAction, timeProvider.GetUtcNow().AddMinutes(1), cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            logger.LogWarning(
                "Provider action {ProviderActionId} for Visit {VisitId} was stopped outside Parkeren; continuation is blocked pending review.",
                candidateAction.ProviderActionId, item.Visit.Id);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        var isProviderActive = string.Equals(confirmedAction.Status, "active", StringComparison.OrdinalIgnoreCase);
        var isProviderScheduled = string.Equals(confirmedAction.Status, "scheduled", StringComparison.OrdinalIgnoreCase);
        if ((!isScheduledAction && !isProviderActive) ||
            (isScheduledAction && !isProviderActive && !isProviderScheduled))
        {
            logger.LogWarning(
                "Provider action {ProviderActionId} for Visit {VisitId} has unexpected status {ProviderStatus}; continuation is blocked.",
                candidateAction.ProviderActionId, item.Visit.Id, confirmedAction.Status);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        if (!ProviderActionMatchPolicy.TimestampsMatch(confirmedAction.Start, candidateAction.PlannedStartAt) ||
            !ProviderActionMatchPolicy.TimestampsMatch(confirmedAction.End, candidateAction.PlannedEndAt))
        {
            logger.LogWarning(
                "Provider action {ProviderActionId} for Visit {VisitId} has start/end {ProviderStartAt}/{ProviderEndAt}, while the locally confirmed start/end is {LocalStartAt}/{LocalEndAt}; continuation is blocked pending review.",
                candidateAction.ProviderActionId, item.Visit.Id, confirmedAction.Start, confirmedAction.End,
                candidateAction.PlannedStartAt, candidateAction.PlannedEndAt);
            await MarkAmbiguousAsync(item, cancellationToken);
            return;
        }

        if (isScheduledAction && isProviderActive)
        {
            var persistedAction = await dbContext.ProviderParkingActions
                .SingleAsync(x => x.Id == candidateAction.Id, cancellationToken);
            persistedAction.ActivateScheduled(confirmedAction.Start, confirmedAction.Status);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (desiredEndAt <= confirmedAction.End)
            return;

        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => (item.Visit.ProviderProductId == null || x.ProviderProductId == item.Visit.ProviderProductId) &&
                        x.ValidFrom < desiredEndAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > confirmedAction.End))
            .ToListAsync(cancellationToken);
        var nextPaid = ProviderCoverageSchedule.NextPaidSegment(confirmedAction.End, desiredEndAt, ruleSets);
        if (nextPaid is null)
            return;

        var dueAt = nextPaid.Start > confirmedAction.End
            ? ProviderCoverageSchedule.PrecheckAt(nextPaid.Start)
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
            .Where(x => (visit.ProviderProductId == null || x.ProviderProductId == visit.ProviderProductId) &&
                        x.ValidFrom < endAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > startAt))
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
                Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage,
                ProviderCoverageSchedule.PrecheckAt(paid.Start)));
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
