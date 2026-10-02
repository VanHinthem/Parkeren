using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderContinuationStartStore(ParkerenDbContext dbContext)
    : IProviderContinuationStartStore
{
    public async Task<ProviderStartPreparation> PrepareInitialCoverageAsync(
        Visit visit, Guid operationId, DateTimeOffset startAt, DateTimeOffset endAt,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || endAt <= startAt)
            throw new ArgumentException("A valid operation and coverage interval are required.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visit.Id);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var persistedVisit = await dbContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        if (persistedVisit.Status != VisitStatus.Active || persistedVisit.Health != VisitHealth.Healthy)
            throw new InvalidOperationException("Initial coverage requires a healthy Active Visit.");

        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(
            x => x.OperationId == operationId, cancellationToken);
        if (existing is not null)
        {
            var action = await dbContext.ProviderParkingActions.SingleAsync(
                x => x.Id == existing.ProviderParkingActionId, cancellationToken);
            if (existing.Type != ProviderOperationType.ContinueStart ||
                existing.VisitId != persistedVisit.Id || action.VisitId != persistedVisit.Id ||
                action.PlannedStartAt != startAt || action.PlannedEndAt != endAt)
                throw new InvalidOperationException("Existing initial coverage does not match this request.");

            var attemptStartedNow = false;
            if (existing.Status == ProviderOperationStatus.Pending && action.State == ProviderActionState.Planned)
            {
                action.MarkStarting();
                existing.BeginAttempt();
                attemptStartedNow = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProviderStartPreparation(existing, action, true, attemptStartedNow);
        }

        if (await dbContext.ProviderParkingActions.AnyAsync(x => x.VisitId == visit.Id &&
                x.State != ProviderActionState.Completed && x.State != ProviderActionState.Stopped,
                cancellationToken) ||
            await dbContext.ProviderOperations.AnyAsync(x => x.VisitId == visit.Id &&
                (x.Status == ProviderOperationStatus.InProgress ||
                 x.Status == ProviderOperationStatus.Unknown ||
                 x.Status == ProviderOperationStatus.Reconciling), cancellationToken))
            throw new InvalidOperationException("Initial provider coverage is not safe to start.");

        var nextAction = new ProviderParkingAction(
            Guid.NewGuid(),
            visit.Id,
            startAt,
            endAt,
            persistedVisit.ProviderProductExternalId,
            persistedVisit.ProviderLocation);
        var operation = new ProviderOperation(Guid.NewGuid(), operationId, visit.Id, nextAction.Id,
            ProviderOperationType.ContinueStart);
        nextAction.MarkStarting();
        operation.BeginAttempt();
        dbContext.ProviderParkingActions.Add(nextAction);
        dbContext.ProviderOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProviderStartPreparation(operation, nextAction, false, true);
    }

    public async Task<ProviderStartPreparation> PrepareAttemptAsync(
        Visit visit,
        ProviderParkingAction precedingAction,
        Guid operationId,
        DateTimeOffset newEndAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(precedingAction);
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation id is required.", nameof(operationId));

        var utcEnd = newEndAt.ToUniversalTime();
        newEndAt = new DateTimeOffset(utcEnd.Ticks - utcEnd.Ticks % 10, TimeSpan.Zero);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visit.Id);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var persistedVisit = await dbContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        if (persistedVisit.Status != VisitStatus.Active)
            throw new InvalidOperationException("A new provider action requires an Active Visit.");

        var previous = await dbContext.ProviderParkingActions.SingleAsync(
            x => x.Id == precedingAction.Id, cancellationToken);
        if (previous.VisitId != persistedVisit.Id || previous.State != ProviderActionState.Active ||
            newEndAt <= previous.PlannedEndAt)
            throw new InvalidOperationException("The preceding provider action does not match this Visit or boundary.");

        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(
            x => x.OperationId == operationId, cancellationToken);
        if (existing is not null)
        {
            var action = existing.ProviderParkingActionId is Guid actionId
                ? await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken)
                : throw new InvalidOperationException("Continuation start has no provider action.");
            if (existing.Type != ProviderOperationType.ContinueStart ||
                existing.VisitId != persistedVisit.Id || action.VisitId != persistedVisit.Id ||
                action.PlannedStartAt != previous.PlannedEndAt.AddSeconds(1) || action.PlannedEndAt != newEndAt)
                throw new InvalidOperationException("Existing continuation start does not match this request.");

            var attemptStartedNow = false;
            if (existing.Status == ProviderOperationStatus.Pending && action.State == ProviderActionState.Planned)
            {
                action.MarkStarting();
                existing.BeginAttempt();
                attemptStartedNow = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProviderStartPreparation(existing, action, true, attemptStartedNow);
        }

        var conflictingMutation = await dbContext.ProviderOperations.AnyAsync(
            x => x.VisitId == persistedVisit.Id &&
                 (x.Status == ProviderOperationStatus.InProgress ||
                  x.Status == ProviderOperationStatus.Unknown ||
                  x.Status == ProviderOperationStatus.Reconciling), cancellationToken);
        if (conflictingMutation)
            throw new InvalidOperationException("Another provider mutation is in progress or requires reconciliation.");

        if (await dbContext.ProviderParkingActions.AnyAsync(
                x => x.VisitId == persistedVisit.Id && x.Id != previous.Id &&
                     x.PlannedStartAt >= previous.PlannedEndAt, cancellationToken))
            throw new InvalidOperationException("A later provider action already exists for this Visit.");

        var nextAction = new ProviderParkingAction(
            Guid.NewGuid(),
            persistedVisit.Id,
            previous.PlannedEndAt.AddSeconds(1),
            newEndAt,
            persistedVisit.ProviderProductExternalId,
            persistedVisit.ProviderLocation);
        var operation = new ProviderOperation(
            Guid.NewGuid(), operationId, persistedVisit.Id, nextAction.Id,
            ProviderOperationType.ContinueStart);
        nextAction.MarkStarting();
        operation.BeginAttempt();
        dbContext.ProviderParkingActions.Add(nextAction);
        dbContext.ProviderOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProviderStartPreparation(operation, nextAction, false, true);
    }
}
