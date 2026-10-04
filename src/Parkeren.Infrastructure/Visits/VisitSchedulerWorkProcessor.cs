using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure.Notifications;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Vehicles;
using Parkeren.Infrastructure.Persistence;
using Parkeren.Infrastructure.ParkingProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitSchedulerWorkProcessor(
    ParkerenDbContext dbContext,
    IProviderExtendStore providerExtendStore,
    IProviderContinuationStartStore continuationStartStore,
    IServiceProvider serviceProvider,
    NotificationInboxWriter inboxWriter,
    TimeProvider timeProvider)
    : IVisitSchedulerWorkProcessor
{
    public async Task ProcessAsync(
        VisitSchedulerWork work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (work.Status != VisitSchedulerWorkStatus.Claimed)
            throw new InvalidOperationException("Only claimed scheduler work can be processed.");

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == work.VisitId, cancellationToken);

        if (work.Type == VisitSchedulerWorkType.ReconcileProviderAction)
        {
            await ProcessProviderActionReconciliationAsync(work, visit, cancellationToken);
            return;
        }

        if (work.Type == VisitSchedulerWorkType.StopVisit)
        {
            await ProcessScheduledStopAsync(work, visit, cancellationToken);
            return;
        }

        if (work.Type == VisitSchedulerWorkType.LongVisitWarning)
        {
            if (visit.Status != VisitStatus.Active)
            {
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var settings = await dbContext.ParkingSystemSettings
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            var occurredAt = timeProvider.GetUtcNow();
            var notificationEvent = new NotificationEvent(
                Guid.NewGuid(),
                NotificationEventType.LongVisitWarning,
                visit.Id,
                occurredAt);
            dbContext.NotificationEvents.Add(notificationEvent);

            var visitor = await dbContext.Users
                .AsNoTracking()
                .SingleAsync(x => x.Id == visit.UserId, cancellationToken);
            var vehicle = await dbContext.Vehicles
                .AsNoTracking()
                .SingleAsync(x => x.Id == visit.VehicleId, cancellationToken);
            var payload = JsonSerializer.Serialize(new LongVisitNotificationPayload(
                visitor.Username,
                vehicle.LicensePlate,
                visit.StartAt,
                occurredAt - visit.StartAt));

            await inboxWriter.WriteAsync(
                notificationEvent,
                NotificationType.LongVisitWarning,
                visit.UserId,
                includeVisitor: true,
                includeAdmins: settings.NotifyAdminOnLongVisit,
                cancellationToken,
                payload);
            work.Complete(occurredAt);

            if (settings.LongVisitReminderInterval is TimeSpan reminderInterval &&
                !await dbContext.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id &&
                         x.Type == VisitSchedulerWorkType.LongVisitWarning &&
                         x.Id != work.Id &&
                         (x.Status == VisitSchedulerWorkStatus.Pending || x.Status == VisitSchedulerWorkStatus.Claimed),
                    cancellationToken))
            {
                dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                    Guid.NewGuid(),
                    visit.Id,
                    VisitSchedulerWorkType.LongVisitWarning,
                    occurredAt + reminderInterval));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (visit.Status != VisitStatus.Active || visit.Health != VisitHealth.Healthy)
        {
            work.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var latestAction = await dbContext.ProviderParkingActions
            .Where(x => x.VisitId == visit.Id &&
                        (x.State == ProviderActionState.Active || x.State == ProviderActionState.Scheduled))
            .OrderByDescending(x => x.PlannedEndAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestAction?.State == ProviderActionState.Scheduled)
        {
            var parkingProvider = serviceProvider.GetRequiredService<IParkingProvider>();
            var remoteActions = string.IsNullOrWhiteSpace(latestAction.ProviderProductId)
                ? await parkingProvider.GetActionsAsync(cancellationToken)
                : await parkingProvider.GetActionsForProductAsync(latestAction.ProviderProductId, cancellationToken);
            var remote = ProviderActionMatchPolicy.FindUniqueMatch(
                remoteActions,
                new ProviderActionMatchCriteria(
                    latestAction.ProviderActionId,
                    latestAction.ProviderProductId));
            if (remote is null)
            {
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (string.Equals(remote.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                var wakeAt = latestAction.PlannedStartAt > timeProvider.GetUtcNow()
                    ? latestAction.PlannedStartAt
                    : timeProvider.GetUtcNow().AddMinutes(1);
                work.Release(wakeAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (!string.Equals(remote.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            latestAction.ActivateScheduled(remote.Start, remote.Status);
            var predecessor = await dbContext.ProviderParkingActions
                .Where(x => x.VisitId == visit.Id &&
                            x.Id != latestAction.Id &&
                            x.State == ProviderActionState.Active &&
                            x.PlannedEndAt < latestAction.PlannedStartAt)
                .OrderByDescending(x => x.PlannedEndAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (predecessor is not null && predecessor.PlannedEndAt < timeProvider.GetUtcNow())
            {
                predecessor.MarkCompleted(predecessor.PlannedEndAt);
                await ProviderActionInitialCostInitializer.TryInitializeAsync(dbContext, predecessor, cancellationToken);
                await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
                    dbContext, predecessor, predecessor.PlannedEndAt.AddMinutes(2), cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (latestAction is null)
        {
            var lastCompletedEnd = await dbContext.ProviderParkingActions.AsNoTracking()
                .Where(x => x.VisitId == visit.Id && x.State == ProviderActionState.Completed)
                .OrderByDescending(x => x.PlannedEndAt)
                .Select(x => (DateTimeOffset?)x.PlannedEndAt)
                .FirstOrDefaultAsync(cancellationToken);
            await ProcessInitialCoverageAsync(work, visit, lastCompletedEnd, cancellationToken);
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (await dbContext.ProviderOperations.AnyAsync(
                x => x.OperationId == work.Id &&
                     x.ProviderParkingActionId == latestAction.Id &&
                     x.Status == ProviderOperationStatus.Succeeded,
                cancellationToken))
        {
            // A previous attempt already confirmed a later provider action.
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        var continuationPrecheckAt = ProviderCoverageSchedule.PrecheckAt(latestAction.PlannedEndAt);
        if (continuationPrecheckAt > now)
        {
            work.Release(continuationPrecheckAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var desiredEndAt = ProviderCoverageSchedule.PlanningEndAt(visit, now);
        if (desiredEndAt <= now ||
            (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan maxElapsed &&
             visit.StartAt + maxElapsed <= now))
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan remainingMaxElapsed)
        {
            var hardEndAt = visit.StartAt + remainingMaxElapsed;
            if (desiredEndAt > hardEndAt)
                desiredEndAt = hardEndAt;

            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var ruleSets = await dbContext.ParkingRuleSets
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => (visit.ProviderProductId == null || x.ProviderProductId == visit.ProviderProductId) &&
                        x.ValidFrom < desiredEndAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
            .ToListAsync(cancellationToken);

        var paidThroughNow = ParkingRuleSetPaidTimeCalculator.Calculate(
            visit.StartAt,
            now,
            ruleSets);

        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaidParkingDuration &&
            paidThroughNow >= maxPaidParkingDuration)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var paidThroughDesiredEnd = ParkingRuleSetPaidTimeCalculator.Calculate(
            visit.StartAt,
            desiredEndAt,
            ruleSets);

        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaidParkingDurationForDesiredEnd &&
            paidThroughDesiredEnd > maxPaidParkingDurationForDesiredEnd)
        {
            desiredEndAt = FindPaidDurationBoundary(
                visit.StartAt,
                desiredEndAt,
                maxPaidParkingDurationForDesiredEnd,
                ruleSets);

            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var nextPaid = ProviderCoverageSchedule.NextPaidSegment(
            latestAction.PlannedEndAt, desiredEndAt, ruleSets);
        if (nextPaid is null)
        {
            if (visit.DesiredEndAt is null &&
                visit.PolicySnapshot.MaxVisitElapsedDuration is null &&
                visit.PolicySnapshot.MaxPaidParkingDuration is null)
                work.Release(desiredEndAt);
            else
                work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (nextPaid.Start > latestAction.PlannedEndAt)
        {
            var nextPaidPrecheckAt = ProviderCoverageSchedule.PrecheckAt(nextPaid.Start);
            if (nextPaidPrecheckAt > now)
            {
                work.Release(nextPaidPrecheckAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (latestAction.PlannedEndAt <= now)
            {
                latestAction.MarkCompleted(latestAction.PlannedEndAt);
                await ProviderActionInitialCostInitializer.TryInitializeAsync(dbContext, latestAction, cancellationToken);
                await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
                    dbContext, latestAction, latestAction.PlannedEndAt.AddMinutes(2), cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await ProcessInitialCoverageAsync(work, visit, latestAction.PlannedEndAt, cancellationToken);
            return;
        }

        var actionRules = ruleSets
            .Where(x => x.ValidFrom <= latestAction.PlannedEndAt &&
                        (x.ValidUntil is null || x.ValidUntil > latestAction.PlannedEndAt))
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefault();
        if (actionRules is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (actionRules.Continuation == ProviderCoverageContinuation.StartNewAction)
        {
            var parkingProvider = serviceProvider.GetService<IParkingProvider>();
            if (parkingProvider is null || string.IsNullOrWhiteSpace(latestAction.ProviderActionId))
            {
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var providerActions = string.IsNullOrWhiteSpace(latestAction.ProviderProductId)
                ? await parkingProvider.GetActionsAsync(cancellationToken)
                : await parkingProvider.GetActionsForProductAsync(latestAction.ProviderProductId, cancellationToken);
            var previousAtProvider = ProviderActionMatchPolicy.FindUniqueMatch(
                providerActions,
                new ProviderActionMatchCriteria(
                    latestAction.ProviderActionId,
                    latestAction.ProviderProductId));
            if (previousAtProvider is null ||
                !string.Equals(previousAtProvider.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                !ProviderActionMatchPolicy.TimestampsMatch(previousAtProvider.End, latestAction.PlannedEndAt))
            {
                if (previousAtProvider?.Status is { } providerStatus &&
                    string.Equals(providerStatus, "stopped", StringComparison.OrdinalIgnoreCase))
                {
                    latestAction.MarkExternallyStopped(providerStatus);
                    await ProviderActionHistoryWorkScheduler.EnsureScheduledAsync(
                        dbContext, latestAction, timeProvider.GetUtcNow().AddMinutes(1), cancellationToken);
                }
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var existing = await dbContext.ProviderOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationId == work.Id, cancellationToken);
            var nextEndAt = existing?.ProviderParkingActionId is Guid existingActionId
                ? (await dbContext.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.Id == existingActionId, cancellationToken)).PlannedEndAt
                : new[] { desiredEndAt, latestAction.PlannedEndAt + actionRules.MaxProviderActionDuration, nextPaid.End }.Min();

            var providerLocation = visit.ProviderLocation;
            if (string.IsNullOrWhiteSpace(providerLocation))
                providerLocation = (await parkingProvider.GetProductAsync(cancellationToken)).Location;

            var executor = serviceProvider.GetService<ContinueVisitStartExecutor>();
            if (string.IsNullOrWhiteSpace(providerLocation) || executor is null)
            {
                work.Release(now.AddMinutes(1));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var licensePlate = await dbContext.Vehicles.AsNoTracking()
                .Where(x => x.Id == visit.VehicleId)
                .Select(x => x.LicensePlate)
                .SingleAsync(cancellationToken);
            var startPreparation = await continuationStartStore.PrepareAttemptAsync(
                visit, latestAction, work.Id, nextEndAt, cancellationToken);
            var startExecution = await executor.ExecuteAsync(
                startPreparation, licensePlate, providerLocation, cancellationToken);
            if (startExecution.RequiresReconciliation)
            {
                work.Release(now.AddMinutes(1));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var maxExistingEndAt = latestAction.PlannedStartAt + actionRules.MaxProviderActionDuration;
        if (desiredEndAt > maxExistingEndAt)
            desiredEndAt = maxExistingEndAt;
        if (desiredEndAt > nextPaid.End)
            desiredEndAt = nextPaid.End;
        if (desiredEndAt <= latestAction.PlannedEndAt)
        {
            visit.SetHealth(VisitHealth.Reconciling);
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var providerExtendExecutor = serviceProvider.GetService<ContinueVisitProviderExecutor>();
        if (providerExtendExecutor is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var preparation = await providerExtendStore.PrepareAttemptAsync(
            visit,
            latestAction,
            work.Id,
            desiredEndAt,
            cancellationToken);

        var execution = await providerExtendExecutor.ExecuteAsync(preparation, cancellationToken);
        if (execution.RequiresReconciliation)
        {
            if (preparation.Operation.Status == ProviderOperationStatus.Unknown)
            {
                var reconciler = serviceProvider.GetService<ContinueVisitProviderReconciler>();
                if (reconciler is not null)
                {
                    var reconciled = await reconciler.ReconcileAsync(preparation, cancellationToken);
                    if (reconciled is not null)
                    {
                        work.Complete(now);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return;
                    }
                }
            }

            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (execution.DefinitiveFailure)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessInitialCoverageAsync(
        VisitSchedulerWork work, Visit visit, DateTimeOffset? fromAt,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var desiredEndAt = ProviderCoverageSchedule.PlanningEndAt(visit, now);
        if (desiredEndAt <= now)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan maxElapsed)
        {
            var hardEnd = visit.StartAt + maxElapsed;
            if (hardEnd < desiredEndAt)
                desiredEndAt = hardEnd;
            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var rules = await dbContext.ParkingRuleSets
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => (visit.ProviderProductId == null || x.ProviderProductId == visit.ProviderProductId) &&
                        x.ValidFrom < desiredEndAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
            .ToListAsync(cancellationToken);
        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaidParkingDurationForInitialCoverage &&
            ParkingRuleSetPaidTimeCalculator.Calculate(visit.StartAt, desiredEndAt, rules) > maxPaidParkingDurationForInitialCoverage)
        {
            desiredEndAt = FindPaidDurationBoundary(
                visit.StartAt, desiredEndAt, maxPaidParkingDurationForInitialCoverage, rules);
            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }
        var paid = ProviderCoverageSchedule.NextPaidSegment(fromAt ?? visit.StartAt, desiredEndAt, rules);
        if (paid is null)
        {
            if (visit.DesiredEndAt is null &&
                visit.PolicySnapshot.MaxVisitElapsedDuration is null &&
                visit.PolicySnapshot.MaxPaidParkingDuration is null)
                work.Release(desiredEndAt);
            else
                work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (paid.Start > now)
        {
            var paidPrecheckAt = ProviderCoverageSchedule.PrecheckAt(paid.Start);
            if (paidPrecheckAt > now)
            {
                work.Release(paidPrecheckAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var actionRules = rules.Single(x => x.ValidFrom <= paid.Start &&
            (x.ValidUntil is null || x.ValidUntil > paid.Start));
        var endAt = paid.End < paid.Start + actionRules.MaxProviderActionDuration
            ? paid.End : paid.Start + actionRules.MaxProviderActionDuration;
        var parkingProvider = serviceProvider.GetRequiredService<IParkingProvider>();
        var providerProductId = visit.ProviderProductExternalId;
        var providerLocation = visit.ProviderLocation;
        if (string.IsNullOrWhiteSpace(providerProductId) || string.IsNullOrWhiteSpace(providerLocation))
        {
            var legacyProduct = await parkingProvider.GetProductAsync(cancellationToken);
            providerProductId = legacyProduct.Id;
            providerLocation = legacyProduct.Location;
        }

        var executor = serviceProvider.GetService<ContinueVisitStartExecutor>();
        if (string.IsNullOrWhiteSpace(providerLocation) || executor is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var licensePlate = await dbContext.Vehicles.AsNoTracking()
            .Where(x => x.Id == visit.VehicleId)
            .Select(x => x.LicensePlate)
            .SingleAsync(cancellationToken);
        if (!await dbContext.ProviderOperations.AnyAsync(x => x.OperationId == work.Id, cancellationToken))
        {
            var remoteActions = string.IsNullOrWhiteSpace(providerProductId)
                ? await parkingProvider.GetActionsAsync(cancellationToken)
                : await parkingProvider.GetActionsForProductAsync(providerProductId, cancellationToken);
            var possibleDuplicate = new ProviderActionMatchCriteria(
                null,
                providerProductId,
                licensePlate,
                ExpectedStart: paid.Start);
            if (remoteActions.Any(action =>
                    ProviderActionMatchPolicy.Matches(action, possibleDuplicate)))
            {
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }
        var preparation = await continuationStartStore.PrepareInitialCoverageAsync(
            visit, work.Id, paid.Start, endAt, cancellationToken);
        var execution = await executor.ExecuteAsync(preparation, licensePlate, providerLocation, cancellationToken);
        if (execution.RequiresReconciliation)
            work.Release(now.AddMinutes(1));
        else
            work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }


    private async Task ProcessProviderActionReconciliationAsync(
        VisitSchedulerWork work,
        Visit visit,
        CancellationToken cancellationToken)
    {
        var actionId = work.ProviderParkingActionId
            ?? throw new InvalidOperationException("Provider-action reconciliation work has no action id.");
        var action = await dbContext.ProviderParkingActions
            .SingleAsync(x => x.Id == actionId && x.VisitId == work.VisitId, cancellationToken);

        if (action.HistoryStatus != ProviderHistoryStatus.Pending)
        {
            work.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        ProviderActionHistoryRecord? record;
        try
        {
            record = await ReadProviderHistoryRecordAsync(action, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TimeoutException or JsonException or TwoParkProviderException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            record = null;
        }
        var now = timeProvider.GetUtcNow();
        if (record is not null && IsTerminalProviderHistoryStatus(record.Status) &&
            record.ActualEndAt >= record.ActualStartAt)
        {
                 var cost = record.ProviderCostAmount is >= 0m
                ? record.ProviderCostAmount
                : null;
            action.ApplyProviderHistory(record.ActualStartAt, record.ActualEndAt, cost);
            if (visit.Status == VisitStatus.Completed)
                await serviceProvider.GetRequiredService<BudgetWarningService>()
                    .EvaluateAsync(visit, cancellationToken, action);
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var historyDeadline = work.CreatedAt.AddMinutes(1).AddHours(6);
        if (now >= historyDeadline)
        {
            action.MarkHistoryIncomplete();
            work.Complete(now);
        }
        else
        {
            var retryDelay = work.AttemptCount switch
            {
                <= 1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(5),
                3 => TimeSpan.FromMinutes(15),
                4 => TimeSpan.FromMinutes(30),
                _ => TimeSpan.FromHours(1)
            };
            var retryAt = now + retryDelay;
            work.Release(retryAt < historyDeadline ? retryAt : historyDeadline);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProviderActionHistoryRecord?> ReadProviderHistoryRecordAsync(
        Parkeren.Domain.Visits.ProviderParkingAction action,
        CancellationToken cancellationToken)
    {
        var reader = serviceProvider.GetService<IProviderActionHistoryReader>();
        if (reader is null || string.IsNullOrWhiteSpace(action.ProviderProductId) ||
            string.IsNullOrWhiteSpace(action.ProviderActionId))
            return null;

        const int pageSize = 10;
        for (var pageNumber = 0; ; pageNumber++)
        {
            var page = await reader.GetActionHistoryPageAsync(
                action.ProviderProductId,
                pageNumber,
                pageSize,
                cancellationToken);
            var matches = page.Records
                .Where(x => string.Equals(x.ProviderActionId, action.ProviderActionId, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
                return matches[0];
            if (matches.Length > 1 || !page.HasMore)
                return null;
        }
    }

    private static bool IsTerminalProviderHistoryStatus(string status) =>
        !string.IsNullOrWhiteSpace(status) &&
        !string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "stopping", StringComparison.OrdinalIgnoreCase);

    private async Task ProcessScheduledStopAsync(
        VisitSchedulerWork work,
        Visit visit,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (visit.Status is VisitStatus.Completed or VisitStatus.Cancelled)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (visit.Status != VisitStatus.Active && visit.Status != VisitStatus.Stopping)
        {
            work.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var claimer = serviceProvider.GetRequiredService<IStopVisitClaimer>();
        var finalizer = serviceProvider.GetRequiredService<IStopVisitFinalizer>();
        var stopStore = serviceProvider.GetRequiredService<IProviderStopStore>();
        var executor = serviceProvider.GetRequiredService<StopVisitProviderExecutor>();

        var command = new StopVisitCommand(work.Id, visit.Id, visit.UserId);
        var claim = await claimer.ClaimAsync(command, cancellationToken);
        if (claim.IsAlreadyCompleted)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        while (await finalizer.RequiresProviderActionAsync(claim, cancellationToken))
        {
            var preparation = await stopStore.PrepareAttemptAsync(claim, cancellationToken);
            var execution = await executor.ExecuteAsync(preparation, cancellationToken);
            if (execution.RequiresReconciliation)
            {
                work.Release(now.AddMinutes(1));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        await finalizer.CompleteWithoutProviderActionAsync(claim, now, cancellationToken);
        work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset FindPaidDurationBoundary(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan maxPaidDuration,
        IReadOnlyCollection<ParkingRuleSet> ruleSets)
    {
        var periods = ParkingRuleSetPeriodSegmenter.Segment(start, end, ruleSets);
        var paid = TimeSpan.Zero;

        foreach (var segment in periods
                     .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
                     .OrderBy(x => x.Start))
        {
            if (!segment.IsPaid)
                continue;

            var duration = segment.End - segment.Start;
            if (paid + duration >= maxPaidDuration)
                return segment.Start + (maxPaidDuration - paid);

            paid += duration;
        }

        return end;
    }
}

internal sealed record LongVisitNotificationPayload(
    string Visitor,
    string LicensePlate,
    DateTimeOffset StartAt,
    TimeSpan ElapsedDuration);
