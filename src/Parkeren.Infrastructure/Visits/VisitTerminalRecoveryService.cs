using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitTerminalRecoveryService(ParkerenDbContext dbContext) : IVisitTerminalRecoveryService
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var visitIds = await dbContext.Visits
            .AsNoTracking()
            .Where(x => x.Status == VisitStatus.Active)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var visitId in visitIds)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var lockKey = VisitAdvisoryLock.For(visitId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                cancellationToken);

            var visit = await dbContext.Visits.SingleAsync(x => x.Id == visitId, cancellationToken);
            if (visit.Status == VisitStatus.Active)
            {
                var ruleSets = await dbContext.ParkingRuleSets
                    .Include(x => x.PaidWindows)
                    .Include(x => x.CalendarExceptions)
                    .Where(x =>
                        (visit.ProviderProductId == null || x.ProviderProductId == visit.ProviderProductId) &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
                    .OrderBy(x => x.ValidFrom)
                    .ToListAsync(cancellationToken);

                await new VisitTerminalWorkPlanner(dbContext)
                    .EnsureAsync(visit, ruleSets, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }
}
