using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ChangeVisitEndTimeOperationalContextResolver(
    ParkerenDbContext dbContext) : IChangeVisitEndTimeOperationalContextResolver
{
    public async Task<ChangeVisitEndTimeOperationalContext?> ResolveAsync(
        DateTimeOffset visitStartedAt,
        DateTimeOffset requestedEndAt,
        CancellationToken cancellationToken = default)
    {
        if (requestedEndAt <= visitStartedAt)
            throw new ArgumentOutOfRangeException(nameof(requestedEndAt));

        var ruleSets = await dbContext.ParkingRuleSets
            .AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < requestedEndAt &&
                        (x.ValidUntil == null || x.ValidUntil > visitStartedAt))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        return ruleSets.Count == 0
            ? null
            : new ChangeVisitEndTimeOperationalContext(ruleSets);
    }
}
