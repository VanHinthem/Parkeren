using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitSchedulerWorkProcessor(ParkerenDbContext dbContext)
    : IVisitSchedulerWorkProcessor
{
    public async Task ProcessAsync(
        VisitSchedulerWork work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (work.Status != VisitSchedulerWorkStatus.Claimed)
            throw new InvalidOperationException("Only claimed scheduler work can be processed.");

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == work.VisitId, cancellationToken);
        if (visit.Status != VisitStatus.Active)
        {
            work.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var latestAction = await dbContext.ProviderParkingActions
            .Where(x => x.VisitId == visit.Id)
            .OrderByDescending(x => x.PlannedEndAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestAction is null)
        {
            work.Complete(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (latestAction.PlannedEndAt > now)
        {
            work.Release(latestAction.PlannedEndAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Provider continuation is deliberately introduced in the next slice.
        // At this point the worker has established that JIT evaluation is due.
        work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
