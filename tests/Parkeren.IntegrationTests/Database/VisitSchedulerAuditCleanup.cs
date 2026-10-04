using Microsoft.EntityFrameworkCore;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

internal static class VisitSchedulerAuditCleanup
{
    public static Task<int> DeleteVisitSchedulerAuditEventsAsync(
        this ParkerenDbContext dbContext,
        CancellationToken cancellationToken,
        params Guid[] visitIds)
    {
        var events = dbContext.VisitSchedulerAuditEvents.AsQueryable();
        if (visitIds.Length > 0)
            events = events.Where(x => visitIds.Contains(x.VisitId));

        return events.ExecuteDeleteAsync(cancellationToken);
    }
}