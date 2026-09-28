using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitSchedulerWorkProcessor(
    ParkerenDbContext dbContext,
    IProviderExtendStore providerExtendStore,
    IServiceProvider serviceProvider)
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

        if (visit.DesiredEndAt is not DateTimeOffset desiredEndAt || desiredEndAt <= now)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var preparation = await providerExtendStore.PrepareAttemptAsync(
            visit,
            latestAction,
            work.Id,
            desiredEndAt,
            cancellationToken);

        var providerExtendExecutor = serviceProvider.GetService<ContinueVisitProviderExecutor>();
        if (providerExtendExecutor is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var execution = await providerExtendExecutor.ExecuteAsync(preparation, cancellationToken);
        if (execution.RequiresReconciliation || execution.DefinitiveFailure)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
