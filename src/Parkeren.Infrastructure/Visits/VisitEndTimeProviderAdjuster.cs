using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitEndTimeProviderAdjuster(
    ParkerenDbContext dbContext,
    IParkingProvider provider,
    IConfiguration configuration) : IVisitEndTimeProviderAdjuster
{
    public async Task<VisitEndTimeProviderAdjustmentResult> AdjustAsync(
        ChangeVisitEndTimeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.DesiredEndAt is not DateTimeOffset requestedEndAt)
            return new(false);

        var visit = await dbContext.Visits.AsNoTracking().SingleAsync(x => x.Id == command.VisitId, cancellationToken);
        var actions = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId == visit.Id &&
                        (x.State == ProviderActionState.Active || x.State == ProviderActionState.Scheduled))
            .ToListAsync(cancellationToken);

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, actions);
        if (impact == VisitEndTimeProviderImpact.None) return new(false);
        if (impact == VisitEndTimeProviderImpact.ShortenActive)
            throw new InvalidOperationException("Requested end shortens an active provider action; that provider mutation strategy is not yet available.");

        var scheduled = actions.Where(x => x.State == ProviderActionState.Scheduled && x.PlannedEndAt > requestedEndAt)
            .OrderBy(x => x.PlannedStartAt).First();
        if (string.IsNullOrWhiteSpace(scheduled.ProviderActionId))
            throw new InvalidOperationException("Scheduled provider action has no provider action id.");

        await provider.StopActionAsync(scheduled.ProviderActionId, cancellationToken);
        var afterCancel = await provider.GetActionsAsync(cancellationToken);
        if (afterCancel.Any(x => x.ProviderActionId == scheduled.ProviderActionId &&
                                 !string.Equals(x.Status, "stopped", StringComparison.OrdinalIgnoreCase)))
            return new(true);

        await MarkScheduledStoppedAsync(visit.Id, scheduled.Id, cancellationToken);

        if (impact == VisitEndTimeProviderImpact.CancelScheduled)
            return new(false);

        var location = configuration["ParkingProvider:Location"];
        if (string.IsNullOrWhiteSpace(location))
            throw new InvalidOperationException("ParkingProvider:Location is required to replace scheduled coverage.");

        var vehicle = await dbContext.Vehicles.AsNoTracking().SingleAsync(x => x.Id == visit.VehicleId, cancellationToken);
        var operationId = Guid.NewGuid();
        var newActionId = Guid.NewGuid();
        await PersistReplacementAttemptAsync(
            visit.Id, newActionId, operationId, scheduled.PlannedStartAt, requestedEndAt, cancellationToken);

        try
        {
            var replacement = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    vehicle.LicensePlate,
                    scheduled.PlannedStartAt,
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await MarkReplacementUnknownAsync(operationId, newActionId, "timeout", cancellationToken);
            return new(true);
        }
        catch (HttpRequestException)
        {
            await MarkReplacementUnknownAsync(operationId, newActionId, "network", cancellationToken);
            return new(true);
        }
    }

    private async Task PersistReplacementAttemptAsync(
        Guid visitId, Guid actionId, Guid operationId, DateTimeOffset startAt, DateTimeOffset endAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(actionId, visitId, startAt, endAt);
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), operationId, visitId, actionId, ProviderOperationType.ContinueStart);
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
        operation.Succeed(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkScheduledStoppedAsync(Guid visitId, Guid actionId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        if (action.State == ProviderActionState.Scheduled)
        {
            action.BeginStopping();
            action.MarkStopped(DateTimeOffset.UtcNow, "stopped");
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
