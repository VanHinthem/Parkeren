using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal static class ProviderActionHistoryWorkScheduler
{
    public static async Task EnsureScheduledAsync(
        ParkerenDbContext dbContext,
        ProviderParkingAction action,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        if (action.State is not (ProviderActionState.Stopped or ProviderActionState.Completed) ||
            action.VisitId is not Guid visitId ||
            string.IsNullOrWhiteSpace(action.ProviderActionId))
            return;

        if (action.HistoryStatus == ProviderHistoryStatus.NotRequired)
            action.ScheduleHistoryReconciliation();
        if (action.HistoryStatus != ProviderHistoryStatus.Pending)
            return;

        var workExists = await dbContext.VisitSchedulerWork.AnyAsync(
            x => x.ProviderParkingActionId == action.Id &&
                 x.Type == VisitSchedulerWorkType.ReconcileProviderAction &&
                 (x.Status == VisitSchedulerWorkStatus.Pending ||
                  x.Status == VisitSchedulerWorkStatus.Claimed),
            cancellationToken);
        if (!workExists)
        {
            dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                Guid.NewGuid(),
                visitId,
                VisitSchedulerWorkType.ReconcileProviderAction,
                dueAt,
                providerParkingActionId: action.Id));
        }
    }
}