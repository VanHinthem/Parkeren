using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStopStore(
    ParkerenDbContext dbContext,
    IProviderOperationExecutionTracker executionTracker) : IProviderStopStore
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
        if (visit.Status != VisitStatus.Stopping)
            throw new InvalidOperationException($"Visit must be Stopping before provider Stop preparation, but was {visit.Status}.");

        var operation = await dbContext.ProviderOperations
            .Where(x => x.VisitId == visit.Id &&
                        x.Type == ProviderOperationType.Stop &&
                        x.Status != ProviderOperationStatus.Succeeded &&
                        x.Status != ProviderOperationStatus.Failed)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (operation is null)
        {
            operation = new ProviderOperation(
                Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, ProviderOperationType.Stop);
            dbContext.ProviderOperations.Add(operation);
        }

        var conflictingMutationExists = await dbContext.ProviderOperations.AnyAsync(
            x => x.VisitId == visit.Id &&
                 x.Id != operation.Id &&
                 x.Type != ProviderOperationType.Stop &&
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
                .OrderByDescending(x => x.ProviderStatus == "scheduled")
                .ThenByDescending(x => x.PlannedStartAt)
                .ToListAsync(cancellationToken);

            if (openActions.Count == 0)
                throw new InvalidOperationException("Provider Stop requires an unresolved provider action.");

            action = openActions[0];
            operation.AttachProviderParkingAction(action.Id);
        }

        var providerActionKnownMissing = await dbContext.ProviderDiscrepancies.AnyAsync(
            x => x.ProviderParkingActionId == action.Id &&
                 x.Type == ProviderDiscrepancyType.MissingProviderAction &&
                 x.Status == ProviderDiscrepancyStatus.Open,
            cancellationToken);

        var attemptStartedNow = false;
        if (operation.Status == ProviderOperationStatus.Pending && action.State is ProviderActionState.Active or ProviderActionState.Scheduled)
        {
            action.BeginStopping();
            operation.BeginAttempt();
            attemptStartedNow = true;
        }

        var executionLease = attemptStartedNow
            ? executionTracker.TryTrack(operation.OperationId)
                ?? throw new InvalidOperationException("Provider Stop attempt is already owned in this process.")
            : null;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProviderStopPreparation(
                operation,
                action,
                !attemptStartedNow,
                attemptStartedNow,
                providerActionKnownMissing,
                executionLease);
        }
        catch
        {
            executionLease?.Dispose();
            throw;
        }
    }
}
