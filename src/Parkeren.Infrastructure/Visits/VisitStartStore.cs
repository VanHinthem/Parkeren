using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitStartStore(ParkerenDbContext dbContext) : IVisitStartStore
{
    public async Task<bool> CancelUnpreparedStartAsync(Guid visitId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var visit = await dbContext.Visits.SingleOrDefaultAsync(x => x.Id == visitId, cancellationToken);
        if (visit is null || visit.Status != VisitStatus.Starting)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var hasProviderOperation = await dbContext.ProviderOperations
            .AnyAsync(x => x.VisitId == visitId, cancellationToken);
        var hasProviderAction = await dbContext.ProviderParkingActions
            .AnyAsync(x => x.VisitId == visitId, cancellationToken);
        if (hasProviderOperation || hasProviderAction)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        visit.SetHealth(VisitHealth.Healthy);
        visit.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SaveAsync(Visit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);

        if (visit.Status == VisitStatus.Active)
        {
            var terminalRuleSets = await LoadTerminalRuleSetsAsync(visit, cancellationToken);
            await new VisitTerminalWorkPlanner(dbContext)
                .EnsureAsync(visit, terminalRuleSets, cancellationToken);

            var longVisitWarningAfter = await dbContext.ParkingSystemSettings
                .AsNoTracking()
                .Select(x => x.LongVisitWarningAfter)
                .SingleAsync(cancellationToken);
            if (longVisitWarningAfter is TimeSpan warningAfter &&
                !await dbContext.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id &&
                         x.Type == VisitSchedulerWorkType.LongVisitWarning &&
                         (x.Status == VisitSchedulerWorkStatus.Pending || x.Status == VisitSchedulerWorkStatus.Claimed),
                    cancellationToken))
            {
                dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                    Guid.NewGuid(),
                    visit.Id,
                    VisitSchedulerWorkType.LongVisitWarning,
                    visit.StartAt + warningAfter));
            }
        }
        if (visit.Status == VisitStatus.Active && visit.DesiredEndAt is DateTimeOffset desiredEndAt &&
            !await dbContext.ProviderParkingActions.AnyAsync(x => x.VisitId == visit.Id, cancellationToken))
        {
            var ruleSets = await dbContext.ParkingRuleSets
                .Include(x => x.PaidWindows)
                .Include(x => x.CalendarExceptions)
                .Where(x => x.ValidFrom < desiredEndAt &&
                            (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
                .ToListAsync(cancellationToken);
            var nextPaid = ProviderCoverageSchedule.NextPaidSegment(visit.StartAt, desiredEndAt, ruleSets);
            if (nextPaid is not null && !await dbContext.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                         (x.Status == VisitSchedulerWorkStatus.Pending || x.Status == VisitSchedulerWorkStatus.Claimed),
                    cancellationToken))
                dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                    Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage,
                    ProviderCoverageSchedule.PrecheckAt(nextPaid.Start)));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<List<ParkingRuleSet>> LoadTerminalRuleSetsAsync(
        Visit visit,
        CancellationToken cancellationToken) =>
        dbContext.ParkingRuleSets
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x =>
                (visit.ProviderProductId == null || x.ProviderProductId == visit.ProviderProductId) &&
                (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);
}
