using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitSchedulerWorkClaimer(ParkerenDbContext dbContext)
    : IVisitSchedulerWorkClaimer
{
    private static readonly TimeSpan DefaultDeferDelay = TimeSpan.FromMinutes(1);

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

        // SCHED-014 will change this to the global Visit-first lock order.
        // For SCHED-015 we only centralize the work-type state/health decision.
        var lockKey = VisitAdvisoryLock.For(work.VisitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == work.VisitId, cancellationToken);
        var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(work.Type, visit.Status, visit.Health);

        switch (decision)
        {
            case VisitSchedulerWorkExecutionDecision.Execute:
                work.Claim(workerId, now);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return work;

            case VisitSchedulerWorkExecutionDecision.Defer:
                work.Defer(now.Add(DefaultDeferDelay));
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;

            case VisitSchedulerWorkExecutionDecision.Cancel:
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unsupported scheduler work execution decision.");
        }
    }

    public async Task ReleaseFailedAsync(
        Guid workId,
        string workerId,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var work = await dbContext.VisitSchedulerWork
            .FromSqlInterpolated($"SELECT w.*, w.xmin FROM visit_scheduler_work AS w WHERE w.\"Id\" = {workId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (work is not null && work.Status == VisitSchedulerWorkStatus.Claimed && work.ClaimedBy == workerId)
        {
            var lockKey = VisitAdvisoryLock.For(work.VisitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

            var visit = await dbContext.Visits.AsNoTracking().SingleAsync(x => x.Id == work.VisitId, cancellationToken);
            if (visit.Status == VisitStatus.Active && visit.Health == VisitHealth.Healthy)
            {
                var claimedAt = work.ClaimedAt ?? throw new InvalidOperationException("Claimed work has no claim time.");
                work.Release(retryAt > claimedAt ? retryAt : claimedAt.AddTicks(1));
            }
            else
                work.Cancel();

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
