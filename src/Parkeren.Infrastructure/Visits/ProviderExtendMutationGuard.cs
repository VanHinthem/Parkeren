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

        if (visit is null || visit.Status != VisitStatus.Active)
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

        var hasOtherMutation = await dbContext.ProviderOperations.AnyAsync(
            x => x.VisitId == visitId &&
                 x.ProviderParkingActionId != providerParkingActionId &&
                 x.Status == ProviderOperationStatus.InProgress,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return !hasOtherMutation;
    }
}
