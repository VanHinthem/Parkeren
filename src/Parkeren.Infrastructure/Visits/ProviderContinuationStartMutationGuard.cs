using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderContinuationStartMutationGuard(ParkerenDbContext dbContext, TimeProvider timeProvider)
    : IProviderContinuationStartMutationGuard
{
    public async Task<bool> CanStartAsync(Guid visitId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var active = await dbContext.Visits.AsNoTracking().AnyAsync(
            x => x.Id == visitId && x.Status == VisitStatus.Active && x.Health == VisitHealth.Healthy,
            cancellationToken);
        var unresolved = await dbContext.ProviderOperations.AsNoTracking()
            .Where(x => x.VisitId == visitId &&
                        (x.Status == ProviderOperationStatus.InProgress ||
                         x.Status == ProviderOperationStatus.Unknown ||
                         x.Status == ProviderOperationStatus.Reconciling))
            .ToListAsync(cancellationToken);

        var allowed = false;
        if (active && unresolved.Count == 1 &&
            unresolved[0].Type == ProviderOperationType.ContinueStart &&
            unresolved[0].Status == ProviderOperationStatus.InProgress &&
            unresolved[0].ProviderParkingActionId is Guid actionId)
        {
            var now = timeProvider.GetUtcNow();
            allowed = await dbContext.ProviderParkingActions.AsNoTracking().AnyAsync(
                x => x.Id == actionId && x.VisitId == visitId &&
                     x.State == ProviderActionState.Starting &&
                     x.PlannedStartAt <= now.AddMinutes(5).AddSeconds(1),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return allowed;
    }
}
