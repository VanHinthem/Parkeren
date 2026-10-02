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

        var candidate = await dbContext.VisitSchedulerWork
            .AsNoTracking()
            .Where(x => x.Status == VisitSchedulerWorkStatus.Pending && x.DueAt <= now)
            .OrderBy(x => x.DueAt)
            .ThenBy(x => x.Type == VisitSchedulerWorkType.StopVisit
                ? 0
                : x.Type == VisitSchedulerWorkType.ContinueProviderCoverage
                    ? 1
                    : 2)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.VisitId })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
            return null;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Global SCHED-014 lock order: Visit advisory lock before scheduler rows.
        var lockKey = VisitAdvisoryLock.For(candidate.VisitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var work = await dbContext.VisitSchedulerWork
            .FromSqlInterpolated($"SELECT w.*, w.xmin FROM visit_scheduler_work AS w WHERE w.\"Id\" = {candidate.Id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null || work.Status != VisitSchedulerWorkStatus.Pending || work.DueAt > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

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
                work.Defer(now.Add(VisitSchedulerWorkExecutionPolicy.DefaultDeferDelay));
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
        var candidateVisitId = await dbContext.VisitSchedulerWork
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => (Guid?)x.VisitId)
            .SingleOrDefaultAsync(cancellationToken);

        if (candidateVisitId is null)
            return;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Keep retry/release on the same Visit-first lock order as claiming and
        // Stop/end-time flows. Never hold the work row while waiting for Visit.
        var lockKey = VisitAdvisoryLock.For(candidateVisitId.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var work = await dbContext.VisitSchedulerWork
            .FromSqlInterpolated($"SELECT w.*, w.xmin FROM visit_scheduler_work AS w WHERE w.\"Id\" = {workId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null || work.Status != VisitSchedulerWorkStatus.Claimed || work.ClaimedBy != workerId)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var visit = await dbContext.Visits.AsNoTracking().SingleAsync(x => x.Id == work.VisitId, cancellationToken);
        var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(work.Type, visit.Status, visit.Health);

        switch (decision)
        {
            case VisitSchedulerWorkExecutionDecision.Execute:
            case VisitSchedulerWorkExecutionDecision.Defer:
            {
                var claimedAt = work.ClaimedAt ?? throw new InvalidOperationException("Claimed work has no claim time.");
                work.Release(retryAt > claimedAt ? retryAt : claimedAt.AddTicks(1));
                break;
            }

            case VisitSchedulerWorkExecutionDecision.Cancel:
                work.Cancel();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unsupported scheduler work execution decision.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
