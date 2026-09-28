using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStartStore(ParkerenDbContext dbContext) : IProviderStartStore
{
    private const long StartOperationLockNamespace = 0x53544152; // STAR
    public async Task<ProviderStartPreparation> PrepareAttemptAsync(StartVisitClaimResult claim, DateTimeOffset providerEndAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var operationLockKey = StartOperationLockNamespace ^ claim.Visit.StartOperationId.GetHashCode();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({operationLockKey})", cancellationToken);

        var visitLockKey = VisitAdvisoryLock.For(claim.Visit.Id);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({visitLockKey})", cancellationToken);

        var conflictingMutationExists = await dbContext.ProviderOperations.AnyAsync(
            x => x.VisitId == claim.Visit.Id &&
                 x.OperationId != claim.Visit.StartOperationId &&
                 (x.Status == ProviderOperationStatus.InProgress ||
                  x.Status == ProviderOperationStatus.Unknown ||
                  x.Status == ProviderOperationStatus.Reconciling),
            cancellationToken);
        if (conflictingMutationExists)
            throw new InvalidOperationException("Another provider mutation is in progress or requires reconciliation for this Visit.");

        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(x => x.OperationId == claim.Visit.StartOperationId, cancellationToken);
        if (existing is not null)
        {
            var existingAction = existing.ProviderParkingActionId is Guid actionId
                ? await dbContext.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken)
                : throw new InvalidOperationException("Existing start operation has no provider action.");
            var attemptStartedNow = false;
            if (existing.Status == ProviderOperationStatus.Pending &&
                existingAction.State == ProviderActionState.Planned)
            {
                existingAction.MarkStarting();
                existing.BeginAttempt();
                attemptStartedNow = true;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProviderStartPreparation(existing, existingAction, true, attemptStartedNow);
        }

        var prepared = new StartVisitProviderPreparer().Prepare(claim, providerEndAt);
        prepared.Action.MarkStarting();
        prepared.Operation.BeginAttempt();
        dbContext.ProviderParkingActions.Add(prepared.Action);
        dbContext.ProviderOperations.Add(prepared.Operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return prepared;
    }
}
