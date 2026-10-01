using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStopResultStore(ParkerenDbContext dbContext) : IProviderStopResultStore
{
    public async Task RecordUnknownAsync(
        ProviderStopPreparation preparation,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.Id == preparation.Operation.Id, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == preparation.Action.Id, cancellationToken);
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == operation.VisitId, cancellationToken);

        if (visit.Status != VisitStatus.Stopping)
            throw new InvalidOperationException("An uncertain provider Stop must keep the Visit in Stopping.");

        action.MarkUnknown();
        operation.MarkUnknown(errorCode);
        visit.SetHealth(VisitHealth.Reconciling);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordConfirmedAsync(
        ProviderStopPreparation preparation,
        ProviderAction providerAction,
        DateTimeOffset actualEndAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.Id == preparation.Operation.Id, cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == preparation.Action.Id, cancellationToken);
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == operation.VisitId, cancellationToken);

        if (providerAction.ProviderActionId != action.ProviderActionId)
            throw new InvalidOperationException("Provider read-back does not match the persisted provider action.");
        if (visit.Status != VisitStatus.Stopping)
            throw new InvalidOperationException("Visit must be Stopping before provider Stop confirmation.");

        if (string.Equals(providerAction.Status, "missing", StringComparison.OrdinalIgnoreCase))
        {
            if (!preparation.ProviderActionKnownMissing)
                throw new InvalidOperationException("A provider action may only be finalized as missing after a recorded missing-action discrepancy.");
            action.MarkProviderMissing();
        }
        else
        {
            if (!string.Equals(providerAction.Status, "stopped", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Provider action is not confirmed stopped.");
            action.MarkStopped(actualEndAt, providerAction.Status);
        }

        operation.Succeed(actualEndAt);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
