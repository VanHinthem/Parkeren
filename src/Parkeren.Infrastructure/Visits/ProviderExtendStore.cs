using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderExtendStore(
    ParkerenDbContext dbContext,
    IProviderOperationExecutionTracker executionTracker) : IProviderExtendStore
{
    public async Task<ProviderExtendPreparation> PrepareAttemptAsync(
        Visit visit,
        ProviderParkingAction action,
        Guid operationId,
        DateTimeOffset providerEndAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(action);

        // PostgreSQL timestamp with time zone has microsecond precision. Normalize
        // before persisting/comparing so a replay of the same .NET instant (100 ns
        // precision) remains idempotent after a database round-trip.
        providerEndAt = NormalizeForPostgres(providerEndAt);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockKey = VisitAdvisoryLock.For(visit.Id);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var persistedVisit = await dbContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        if (persistedVisit.Status != VisitStatus.Active)
            throw new InvalidOperationException("Provider continuation can only be prepared for an Active Visit.");

        var otherMutationExists = await dbContext.ProviderOperations.AnyAsync(
            x => x.VisitId == persistedVisit.Id &&
                 x.OperationId != operationId &&
                 (x.Status == ProviderOperationStatus.InProgress ||
                  x.Status == ProviderOperationStatus.Unknown ||
                  x.Status == ProviderOperationStatus.Reconciling),
            cancellationToken);
        if (otherMutationExists)
            throw new InvalidOperationException("Another provider mutation is in progress or requires reconciliation for this Visit.");

        var persistedAction = await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
        if (persistedAction.VisitId != persistedVisit.Id || persistedAction.State != ProviderActionState.Active)
            throw new InvalidOperationException("Provider continuation requires the Visit's active provider action.");

        var existing = await dbContext.ProviderOperations
            .SingleOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Type != ProviderOperationType.Extend ||
                existing.VisitId != persistedVisit.Id ||
                existing.ProviderParkingActionId != persistedAction.Id)
                throw new InvalidOperationException("Existing provider operation does not match this continuation.");

            if (existing.RequestedEndAt.HasValue && NormalizeForPostgres(existing.RequestedEndAt.Value) != providerEndAt)
                throw new InvalidOperationException("Existing provider continuation has a different requested end.");

            if (!existing.RequestedEndAt.HasValue && existing.Status == ProviderOperationStatus.Pending)
                existing.SetRequestedEndAt(providerEndAt);

            var attemptStartedNow = false;
            if (existing.Status == ProviderOperationStatus.Pending)
            {
                existing.BeginAttempt();
                attemptStartedNow = true;
            }

            var executionLease = attemptStartedNow
                ? executionTracker.TryTrack(existing.OperationId)
                    ?? throw new InvalidOperationException("Provider Extend attempt is already owned in this process.")
                : null;
            try
            {
                if (attemptStartedNow)
                    await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ProviderExtendPreparation(
                    existing,
                    persistedAction,
                    providerEndAt,
                    true,
                    attemptStartedNow,
                    executionLease);
            }
            catch
            {
                executionLease?.Dispose();
                throw;
            }
        }

        var prepared = new ContinueVisitProviderPreparer()
            .Prepare(persistedVisit, persistedAction, operationId, providerEndAt);

        prepared.Operation.SetRequestedEndAt(providerEndAt);
        prepared.Operation.BeginAttempt();
        dbContext.ProviderOperations.Add(prepared.Operation);
        var preparedLease = executionTracker.TryTrack(prepared.Operation.OperationId)
            ?? throw new InvalidOperationException("Provider Extend attempt is already owned in this process.");
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return prepared with { AttemptStartedNow = true, ExecutionLease = preparedLease };
        }
        catch
        {
            preparedLease.Dispose();
            throw;
        }
    }

    private static DateTimeOffset NormalizeForPostgres(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
