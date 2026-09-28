using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitRecoveryService(
    ParkerenDbContext dbContext,
    ContinueVisitProviderReconciler extendReconciler) : IVisitRecoveryService
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var items = await LoadAsync(cancellationToken);

        foreach (var item in items)
        {
            var decision = VisitRecoveryClassifier.Classify(item);
            if (decision.Kind != VisitRecoveryKind.ReconcileExtend ||
                decision.Operation is null ||
                decision.Operation.Status != ProviderOperationStatus.Unknown ||
                decision.Operation.ProviderParkingActionId is not Guid actionId ||
                decision.Operation.RequestedEndAt is not DateTimeOffset requestedEndAt)
                continue;

            var action = item.ProviderActions.SingleOrDefault(x => x.Id == actionId);
            if (action is null)
                continue;

            var preparation = new ProviderExtendPreparation(
                decision.Operation,
                action,
                requestedEndAt,
                true,
                false);

            await extendReconciler.ReconcileAsync(preparation, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<VisitRecoveryItem>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var visits = await dbContext.Visits
            .AsNoTracking()
            .Where(x => x.Status == VisitStatus.Starting ||
                        x.Status == VisitStatus.Active ||
                        x.Status == VisitStatus.Stopping)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        if (visits.Count == 0)
            return [];

        var visitIds = visits.Select(x => x.Id).ToArray();

        var actions = await dbContext.ProviderParkingActions
            .AsNoTracking()
            .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
            .ToListAsync(cancellationToken);

        var operations = await dbContext.ProviderOperations
            .AsNoTracking()
            .Where(x => x.VisitId.HasValue &&
                        visitIds.Contains(x.VisitId.Value) &&
                        (x.Status == ProviderOperationStatus.Pending ||
                         x.Status == ProviderOperationStatus.InProgress ||
                         x.Status == ProviderOperationStatus.Unknown ||
                         x.Status == ProviderOperationStatus.Reconciling))
            .ToListAsync(cancellationToken);

        return visits.Select(visit => new VisitRecoveryItem(
            visit,
            actions.Where(x => x.VisitId == visit.Id).ToArray(),
            operations.Where(x => x.VisitId == visit.Id).ToArray()))
            .ToArray();
    }
}
