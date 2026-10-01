using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitStartStore(ParkerenDbContext dbContext) : IVisitStartStore
{
    public async Task SaveAsync(Visit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);

        if (visit.Status == VisitStatus.Active)
        {
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
                    Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, nextPaid.Start));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
