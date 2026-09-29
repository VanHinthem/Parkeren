using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderExtendMutationGuard(ParkerenDbContext dbContext)
    : IProviderExtendMutationGuard
{
    public async Task<bool> CanExtendAsync(
        Guid visitId,
        Guid providerParkingActionId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var visit = await dbContext.Visits.SingleOrDefaultAsync(
            x => x.Id == visitId,
            cancellationToken);

        if (visit is null || visit.Status != VisitStatus.Active || visit.Health != VisitHealth.Healthy)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var action = await dbContext.ProviderParkingActions.SingleOrDefaultAsync(
            x => x.Id == providerParkingActionId,
            cancellationToken);

        if (action is null ||
            action.VisitId != visitId ||
            action.State != ProviderActionState.Active ||
            string.IsNullOrWhiteSpace(action.ProviderActionId))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var unresolvedOperations = await dbContext.ProviderOperations
            .Where(x => x.VisitId == visitId &&
                        (x.Status == ProviderOperationStatus.InProgress ||
                         x.Status == ProviderOperationStatus.Unknown ||
                         x.Status == ProviderOperationStatus.Reconciling))
            .ToListAsync(cancellationToken);

        var hasConflictingMutation = unresolvedOperations.Any(x =>
            x.Status != ProviderOperationStatus.InProgress ||
            x.Type != ProviderOperationType.Extend ||
            x.ProviderParkingActionId != providerParkingActionId);

        var matchingExtendCount = unresolvedOperations.Count(x =>
            x.Status == ProviderOperationStatus.InProgress &&
            x.Type == ProviderOperationType.Extend &&
            x.ProviderParkingActionId == providerParkingActionId);

        await transaction.CommitAsync(cancellationToken);
        return !hasConflictingMutation && matchingExtendCount == 1;
    }
}
