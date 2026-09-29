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
        var replacement = await provider.StartActionAsync(
            new ProviderStartRequest(vehicle.LicensePlate, location, requestedEndAt, scheduled.PlannedStartAt),
            cancellationToken);
        var readBack = await provider.GetActionsAsync(cancellationToken);
        var confirmed = readBack.SingleOrDefault(x => x.ProviderActionId == replacement.ProviderActionId);
        if (confirmed is null || !string.Equals(confirmed.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            return new(true);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visit.Id);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var newAction = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, scheduled.PlannedStartAt, requestedEndAt);
        newAction.MarkStarting();
        newAction.MarkScheduled(confirmed.ProviderActionId, confirmed.Status);
        dbContext.ProviderParkingActions.Add(newAction);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(false);
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
