using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitEndTimeProviderAdjuster(
    ParkerenDbContext dbContext,
    IParkingProvider provider,
    TimeProvider timeProvider,
    IProviderOperationExecutionTracker executionTracker) : IVisitEndTimeProviderAdjuster
{
    public async Task<VisitEndTimeProviderAdjustmentResult> AdjustAsync(
        ChangeVisitEndTimeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.DesiredEndAt is not DateTimeOffset requestedEndAt)
            return new(false);

        var visit = await dbContext.Visits.AsNoTracking().SingleAsync(x => x.Id == command.VisitId, cancellationToken);
        var rootChange = await dbContext.VisitEndTimeChanges.AsNoTracking()
            .SingleAsync(x => x.OperationId == command.OperationId, cancellationToken);

        var existingChildren = await dbContext.ProviderOperations.AsNoTracking()
            .Where(x => x.ParentOperationId == rootChange.OperationId)
            .ToListAsync(cancellationToken);

        if (existingChildren.Any(x => x.Status is ProviderOperationStatus.Pending
                                     or ProviderOperationStatus.InProgress
                                     or ProviderOperationStatus.Unknown
                                     or ProviderOperationStatus.Reconciling))
            return new(true);

        var actions = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId == visit.Id &&
                        (x.State == ProviderActionState.Active || x.State == ProviderActionState.Scheduled))
            .ToListAsync(cancellationToken);

        var successfulCancel = existingChildren.SingleOrDefault(
            x => x.Type == ProviderOperationType.Stop &&
                 x.Status == ProviderOperationStatus.Succeeded &&
                 x.ProviderParkingActionId.HasValue);

        Parkeren.Domain.Visits.ProviderParkingAction? scheduled = null;
        VisitEndTimeProviderImpact impact;

        if (successfulCancel?.ProviderParkingActionId is Guid cancelledActionId)
        {
            var cancelledAction = await dbContext.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.Id == cancelledActionId, cancellationToken);

            if (cancelledAction.PlannedStartAt >= requestedEndAt)
                return new(false);

            if (cancelledAction.PlannedEndAt > requestedEndAt)
            {
                impact = VisitEndTimeProviderImpact.ReplaceScheduled;
                scheduled = cancelledAction;
            }
            else
            {
                impact = VisitEndTimeProviderImpact.None;
            }
        }
        else
        {
            impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, actions);
            if (impact == VisitEndTimeProviderImpact.ShortenActive)
            {
                // 2Park cannot change the end time of an active action. The active action is
                // stopped durably by scheduler work at the new Visit end. Any scheduled
                // successor that would outlive that end must be cancelled now.
                scheduled = actions
                    .Where(x => x.State == ProviderActionState.Scheduled && x.PlannedEndAt > requestedEndAt)
                    .OrderBy(x => x.PlannedStartAt)
                    .FirstOrDefault();

                if (scheduled is null)
                    return new(false);

                impact = VisitEndTimeProviderImpact.CancelScheduled;
            }
            else if (impact != VisitEndTimeProviderImpact.None)
            {
                scheduled = actions.Where(x => x.State == ProviderActionState.Scheduled && x.PlannedEndAt > requestedEndAt)
                    .OrderBy(x => x.PlannedStartAt).First();
            }

            if (scheduled is not null && string.IsNullOrWhiteSpace(scheduled.ProviderActionId))
                throw new InvalidOperationException("Scheduled provider action has no provider action id.");
        }

        if (impact == VisitEndTimeProviderImpact.None)
            return new(false);

        var scheduledProviderActionId = scheduled!.ProviderActionId
            ?? throw new InvalidOperationException("Scheduled provider action has no provider action id.");

        var existingCancel = existingChildren.SingleOrDefault(
            x => x.Type == ProviderOperationType.Stop &&
                 x.ProviderParkingActionId == scheduled.Id);

        if (existingCancel is null)
        {
            var cancelOperationId = Guid.NewGuid();
            using var activeAttempt = await PersistScheduledCancelAttemptAsync(
                visit.Id, scheduled.Id, cancelOperationId, rootChange.OperationId, cancellationToken);

            try
            {
                if (string.IsNullOrWhiteSpace(scheduled.ProviderProductId))
                    await provider.StopActionAsync(scheduledProviderActionId, cancellationToken);
                else
                    await provider.StopActionForProductAsync(
                        scheduled.ProviderProductId,
                        scheduledProviderActionId,
                        cancellationToken);

                var afterCancel = string.IsNullOrWhiteSpace(scheduled.ProviderProductId)
                    ? await provider.GetActionsAsync(cancellationToken)
                    : await provider.GetActionsForProductAsync(scheduled.ProviderProductId, cancellationToken);
                var matchingActions = afterCancel
                    .Where(x => x.ProviderActionId == scheduledProviderActionId)
                    .ToArray();
                if (matchingActions.Any(x => !string.Equals(x.Status, "stopped", StringComparison.OrdinalIgnoreCase)))
                {
                    await MarkScheduledCancelUnknownAsync(
                        visit.Id, scheduled!.Id, cancelOperationId, "read-back-unconfirmed", CancellationToken.None);
                    return new(true);
                }

                await ConfirmScheduledCancelAsync(
                    visit.Id,
                    scheduled!.Id,
                    cancelOperationId,
                    matchingActions.Length == 1 ? matchingActions[0].Start : null,
                    CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await MarkScheduledCancelUnknownAsync(
                    visit.Id, scheduled!.Id, cancelOperationId, "timeout", CancellationToken.None);
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return new(true);
            }
            catch (HttpRequestException)
            {
                await MarkScheduledCancelUnknownAsync(
                    visit.Id, scheduled!.Id, cancelOperationId, "network", CancellationToken.None);
                return new(true);
            }
        }
        else if (existingCancel.Status != ProviderOperationStatus.Succeeded)
        {
            return new(true);
        }

        if (impact == VisitEndTimeProviderImpact.CancelScheduled)
            return new(false);

        var providerProductId = visit.ProviderProductExternalId;
        var location = visit.ProviderLocation;
        if (string.IsNullOrWhiteSpace(providerProductId) || string.IsNullOrWhiteSpace(location))
        {
            var legacyProduct = await provider.GetProductAsync(cancellationToken);
            providerProductId = legacyProduct.Id;
            location = legacyProduct.Location;
        }

        var existingReplacement = existingChildren.SingleOrDefault(
            x => x.Type == ProviderOperationType.ContinueStart &&
                 x.RequestedEndAt == requestedEndAt);

        if (existingReplacement is not null)
            return new(existingReplacement.Status != ProviderOperationStatus.Succeeded);

        var vehicle = await dbContext.Vehicles.AsNoTracking().SingleAsync(x => x.Id == visit.VehicleId, cancellationToken);
        var operationId = Guid.NewGuid();
        var newActionId = Guid.NewGuid();
        using var activeReplacement = await PersistReplacementAttemptAsync(
            visit.Id, newActionId, operationId, rootChange.OperationId,
            scheduled!.PlannedStartAt, requestedEndAt,
            providerProductId, location, cancellationToken);

        try
        {
            var replacement = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    vehicle.LicensePlate,
                    scheduled!.PlannedStartAt,
                    requestedEndAt,
                    location,
                    providerProductId),
                cancellationToken);
            var readBack = await provider.GetActionsForProductAsync(providerProductId, cancellationToken);
            var confirmed = readBack.SingleOrDefault(x => x.ProviderActionId == replacement.ProviderActionId);
            if (confirmed is null || !string.Equals(confirmed.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                await MarkReplacementUnknownAsync(operationId, newActionId, "read-back-unconfirmed", cancellationToken);
                return new(true);
            }

            await ConfirmReplacementAsync(operationId, newActionId, confirmed, cancellationToken);
            return new(false);
        }
        catch (OperationCanceledException)
        {
            await MarkReplacementUnknownAsync(operationId, newActionId, "timeout", CancellationToken.None);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new(true);
        }
        catch (HttpRequestException)
        {
            await MarkReplacementUnknownAsync(operationId, newActionId, "network", cancellationToken);
            return new(true);
        }
    }

    private async Task<IDisposable> PersistReplacementAttemptAsync(
        Guid visitId, Guid actionId, Guid operationId, Guid parentOperationId,
        DateTimeOffset startAt, DateTimeOffset endAt,
        string providerProductId, string providerLocation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var persistedVisit = await dbContext.Visits.AsNoTracking()
            .SingleAsync(x => x.Id == visitId, cancellationToken);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            actionId,
            visitId,
            startAt,
            endAt,
            providerProductId,
            providerLocation);
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), operationId, visitId, actionId, ProviderOperationType.ContinueStart);
        operation.SetParentOperationId(parentOperationId);
        operation.SetRequestedEndAt(endAt);
        operation.BeginAttempt();
        var executionLease = executionTracker.TryTrack(operation.OperationId)
            ?? throw new InvalidOperationException("Provider operation is already owned in this process.");
        dbContext.ProviderParkingActions.Add(action);
        dbContext.ProviderOperations.Add(operation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return executionLease;
        }
        catch
        {
            executionLease.Dispose();
            throw;
        }
    }

    private async Task MarkReplacementUnknownAsync(
        Guid operationId, Guid actionId, string errorCode, CancellationToken cancellationToken)
    {
        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        action.MarkUnknown();
        operation.MarkUnknown(errorCode);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ConfirmReplacementAsync(
        Guid operationId, Guid actionId, Parkeren.Application.ParkingProvider.ProviderParkingAction confirmed,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        action.MarkScheduled(confirmed.ProviderActionId, confirmed.Status);
        operation.Succeed(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IDisposable> PersistScheduledCancelAttemptAsync(
        Guid visitId, Guid actionId, Guid operationId, Guid parentOperationId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        if (action.State != ProviderActionState.Scheduled)
            throw new InvalidOperationException("Only a scheduled provider action can be cancelled for an end-time change.");

        var operation = new ProviderOperation(
            Guid.NewGuid(), operationId, visitId, actionId, ProviderOperationType.Stop);
        operation.SetParentOperationId(parentOperationId);
        action.BeginStopping();
        operation.BeginAttempt();
        var executionLease = executionTracker.TryTrack(operation.OperationId)
            ?? throw new InvalidOperationException("Provider operation is already owned in this process.");
        dbContext.ProviderOperations.Add(operation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return executionLease;
        }
        catch
        {
            executionLease.Dispose();
            throw;
        }
    }

    private async Task MarkScheduledCancelUnknownAsync(
        Guid visitId, Guid actionId, Guid operationId, string errorCode, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        action.MarkUnknown();
        operation.MarkUnknown(errorCode);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ConfirmScheduledCancelAsync(
        Guid visitId,
        Guid actionId,
        Guid operationId,
        DateTimeOffset? providerStartedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        var stoppedAt = timeProvider.GetUtcNow();
        action.MarkStopped(stoppedAt, "stopped", providerStartedAt);
        await ProviderActionInitialCostInitializer.TryInitializeAsync(dbContext, action, cancellationToken);
        operation.Succeed(stoppedAt);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

}
