using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStopStore(ParkerenDbContext dbContext) : IProviderStopStore
{
    public async Task<ProviderStopPreparation> PrepareAttemptAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Operation is null || claim.Operation.Type != ProviderOperationType.Stop)
            throw new InvalidOperationException("A persisted Stop operation is required.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(claim.Visit.Id);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == claim.Visit.Id, cancellationToken);
        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.Id == claim.Operation.Id, cancellationToken);
        if (visit.Status != VisitStatus.Stopping)
            throw new InvalidOperationException($"Visit must be Stopping before provider Stop preparation, but was {visit.Status}.");

        var conflictingMutationExists = await dbContext.ProviderOperations.AnyAsync(
            x => x.VisitId == visit.Id &&
                 x.Id != operation.Id &&
                 (x.Status == ProviderOperationStatus.InProgress ||
                  x.Status == ProviderOperationStatus.Unknown ||
                  x.Status == ProviderOperationStatus.Reconciling),
            cancellationToken);
        if (conflictingMutationExists)
            throw new InvalidOperationException("Another provider mutation is in progress or requires reconciliation for this Visit.");

        ProviderParkingAction action;
        if (operation.ProviderParkingActionId is Guid actionId)
        {
            action = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        }
        else
        {
            var openActions = await dbContext.ProviderParkingActions
                .Where(x => x.VisitId == visit.Id &&
                            x.State != ProviderActionState.Stopped &&
                            x.State != ProviderActionState.Completed &&
                            x.State != ProviderActionState.Failed)
                .ToListAsync(cancellationToken);

            if (openActions.Count != 1)
                throw new InvalidOperationException($"Provider Stop requires exactly one unresolved provider action, but found {openActions.Count}.");

            action = openActions[0];
            operation.AttachProviderParkingAction(action.Id);
        }

        var attemptStartedNow = false;
        if (operation.Status == ProviderOperationStatus.Pending && action.State == ProviderActionState.Active)
        {
            action.BeginStopping();
            operation.BeginAttempt();
            attemptStartedNow = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ProviderStopPreparation(operation, action, !attemptStartedNow, attemptStartedNow);
    }
}
