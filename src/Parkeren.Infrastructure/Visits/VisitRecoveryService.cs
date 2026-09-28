using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitRecoveryService(
    ParkerenDbContext dbContext,
    ContinueVisitProviderReconciler extendReconciler,
    StopVisitProviderReconciler stopReconciler,
    StartVisitProviderReconciler startReconciler) : IVisitRecoveryService
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var items = await LoadAsync(cancellationToken);

        foreach (var item in items)
        {
            var decision = VisitRecoveryClassifier.Classify(item);

            if (decision.Kind == VisitRecoveryKind.RebuildScheduler)
            {
                await RebuildSchedulerAsync(item, cancellationToken);
                continue;
            }

            if (decision.Operation is null ||
                decision.Operation.Status != ProviderOperationStatus.Unknown ||
                decision.Operation.ProviderParkingActionId is not Guid actionId)
                continue;

            var action = item.ProviderActions.SingleOrDefault(x => x.Id == actionId);
            if (action is null)
                continue;

            if (decision.Kind == VisitRecoveryKind.ReconcileStart)
            {
                var licensePlate = await dbContext.Vehicles
                    .AsNoTracking()
                    .Where(x => x.Id == item.Visit.VehicleId)
                    .Select(x => x.LicensePlate)
                    .SingleOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(licensePlate))
                    continue;

                var preparation = new ProviderStartPreparation(
                    decision.Operation,
                    action,
                    true,
                    false);

                await startReconciler.ReconcileAsync(preparation, licensePlate, cancellationToken);
            }
            else if (decision.Kind == VisitRecoveryKind.ReconcileExtend &&
                decision.Operation.RequestedEndAt is DateTimeOffset requestedEndAt)
            {
                var preparation = new ProviderExtendPreparation(
                    decision.Operation,
                    action,
                    requestedEndAt,
                    true,
                    false);

                await extendReconciler.ReconcileAsync(preparation, cancellationToken);
            }
            else if (decision.Kind == VisitRecoveryKind.ReconcileStop)
            {
                var preparation = new ProviderStopPreparation(
                    decision.Operation,
                    action,
                    true,
                    false);

                await stopReconciler.ReconcileAsync(preparation, cancellationToken);
            }
        }
    }


    private async Task RebuildSchedulerAsync(
        VisitRecoveryItem item,
        CancellationToken cancellationToken)
    {
        if (item.Visit.Status != VisitStatus.Active ||
            item.Visit.DesiredEndAt is not DateTimeOffset desiredEndAt)
            return;

        var activeAction = item.ProviderActions
            .Where(x => x.State == ProviderActionState.Active)
            .OrderByDescending(x => x.PlannedEndAt)
            .FirstOrDefault();

        if (activeAction is null || desiredEndAt <= activeAction.PlannedEndAt)
            return;

        var dueAt = activeAction.PlannedEndAt;
        var exists = await dbContext.VisitSchedulerWork.AnyAsync(
            x => x.VisitId == item.Visit.Id &&
                 x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                 (x.Status == VisitSchedulerWorkStatus.Pending ||
                  x.Status == VisitSchedulerWorkStatus.Claimed) &&
                 x.DueAt == dueAt,
            cancellationToken);

        if (exists)
            return;

        dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
            Guid.NewGuid(),
            item.Visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            dueAt));

        await dbContext.SaveChangesAsync(cancellationToken);
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
