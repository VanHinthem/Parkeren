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

        // The first processor slice only establishes durable worker completion.
        // JIT provider continuation is added separately so provider mutations are
        // never introduced without their reconciliation/idempotency safeguards.
        work.Complete(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
