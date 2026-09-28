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
                SELECT *
                FROM visit_scheduler_work
                WHERE "Status" = 'Pending' AND "DueAt" <= {now}
                ORDER BY "DueAt", "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        work.Claim(workerId, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }
}
