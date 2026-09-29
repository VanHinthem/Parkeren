using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitEndTimeProviderAdjuster(
    ParkerenDbContext dbContext,
    IParkingProvider provider,
    TimeProvider timeProvider) : IVisitEndTimeProviderAdjuster
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
                throw new InvalidOperationException("Requested end shortens an active provider action; that provider mutation strategy is not yet available.");

            if (impact != VisitEndTimeProviderImpact.None)
            {
                scheduled = actions.Where(x => x.State == ProviderActionState.Scheduled && x.PlannedEndAt > requestedEndAt)
                    .OrderBy(x => x.PlannedStartAt).First();
                if (string.IsNullOrWhiteSpace(scheduled!.ProviderActionId))
                    throw new InvalidOperationException("Scheduled provider action has no provider action id.");
            }
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
            await PersistScheduledCancelAttemptAsync(
                visit.Id, scheduled.Id, cancelOperationId, rootChange.OperationId, cancellationToken);

            try
            {
                await provider.StopActionAsync(scheduledProviderActionId, cancellationToken);
            var afterCancel = await provider.GetActionsAsync(cancellationToken);
            if (afterCancel.Any(x => x.ProviderActionId == scheduledProviderActionId &&
                                     !string.Equals(x.Status, "stopped", StringComparison.OrdinalIgnoreCase)))
            {
                await MarkScheduledCancelUnknownAsync(
                    visit.Id, scheduled!.Id, cancelOperationId, "read-back-unconfirmed", CancellationToken.None);
                return new(true);
            }

                await ConfirmScheduledCancelAsync(
                    visit.Id, scheduled!.Id, cancelOperationId, CancellationToken.None);
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

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Location))
            throw new InvalidOperationException("Parking provider product has no location configured.");
        var location = product.Location;

        var existingReplacement = existingChildren.SingleOrDefault(
            x => x.Type == ProviderOperationType.ContinueStart &&
                 x.RequestedEndAt == requestedEndAt);

        if (existingReplacement is not null)
            return new(existingReplacement.Status != ProviderOperationStatus.Succeeded);

        var vehicle = await dbContext.Vehicles.AsNoTracking().SingleAsync(x => x.Id == visit.VehicleId, cancellationToken);
        var operationId = Guid.NewGuid();
        var newActionId = Guid.NewGuid();
        await PersistReplacementAttemptAsync(
            visit.Id, newActionId, operationId, rootChange.OperationId,
            scheduled!.PlannedStartAt, requestedEndAt, cancellationToken);

        try
        {
            var replacement = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    vehicle.LicensePlate,
                    scheduled!.PlannedStartAt,
                    requestedEndAt,
                    location),
                cancellationToken);
            var readBack = await provider.GetActionsAsync(cancellationToken);
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

    private async Task PersistReplacementAttemptAsync(
        Guid visitId, Guid actionId, Guid operationId, Guid parentOperationId,
        DateTimeOffset startAt, DateTimeOffset endAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(actionId, visitId, startAt, endAt);
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), operationId, visitId, actionId, ProviderOperationType.ContinueStart);
        operation.SetParentOperationId(parentOperationId);
        operation.SetRequestedEndAt(endAt);
        operation.BeginAttempt();
        dbContext.ProviderParkingActions.Add(action);
        dbContext.ProviderOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    private async Task PersistScheduledCancelAttemptAsync(
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
        dbContext.ProviderOperations.Add(operation);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
        Guid visitId, Guid actionId, Guid operationId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        action.MarkStopped(timeProvider.GetUtcNow(), "stopped");
        operation.Succeed(timeProvider.GetUtcNow());

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

}
