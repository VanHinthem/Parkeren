using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitSchedulerWorkClaimer(ParkerenDbContext dbContext)
    : IVisitSchedulerWorkClaimer
{
    public async Task<VisitSchedulerWork?> ClaimNextDueAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker id is required.", nameof(workerId));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var work = await dbContext.VisitSchedulerWork
            .FromSqlInterpolated($"""
                SELECT w.*, w.xmin
                FROM visit_scheduler_work AS w
                WHERE w."Status" = 'Pending' AND w."DueAt" <= {now}
                ORDER BY w."DueAt", w."CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        // Revalidate the owning Visit while the work item is locked. Stop wins over
        // continuation: once a Visit has left Active state, pending continuation
        // work is cancelled instead of being handed to a worker.
        var visit = await dbContext.Visits.SingleAsync(x => x.Id == work.VisitId, cancellationToken);
        if (visit.Status != VisitStatus.Active)
        {
            work.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        work.Claim(workerId, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }
}
