using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderExtendStore(ParkerenDbContext dbContext) : IProviderExtendStore
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
                 x.Status == ProviderOperationStatus.InProgress &&
                 x.OperationId != operationId,
            cancellationToken);
        if (otherMutationExists)
            throw new InvalidOperationException("Another provider mutation is already in progress for this Visit.");

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

            if (existing.RequestedEndAt.HasValue && existing.RequestedEndAt.Value != providerEndAt)
                throw new InvalidOperationException("Existing provider continuation has a different requested end.");

            if (!existing.RequestedEndAt.HasValue && existing.Status == ProviderOperationStatus.Pending)
                existing.SetRequestedEndAt(providerEndAt);

            var attemptStartedNow = false;
            if (existing.Status == ProviderOperationStatus.Pending)
            {
                existing.BeginAttempt();
                attemptStartedNow = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProviderExtendPreparation(
                existing,
                persistedAction,
                providerEndAt,
                true,
                attemptStartedNow);
        }

        var prepared = new ContinueVisitProviderPreparer()
            .Prepare(persistedVisit, persistedAction, operationId, providerEndAt);

        prepared.Operation.SetRequestedEndAt(providerEndAt);
        prepared.Operation.BeginAttempt();
        dbContext.ProviderOperations.Add(prepared.Operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return prepared with { AttemptStartedNow = true };
    }
}
