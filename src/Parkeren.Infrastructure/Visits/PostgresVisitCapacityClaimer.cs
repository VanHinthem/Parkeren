using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitCapacityClaimer(ParkerenDbContext dbContext) : IVisitCapacityClaimer
{
    private const long CapacityLockKey = 0x5041524B; // PARK

    public async Task<VisitCapacityClaim> TryClaimAsync(Visit visit, int maxConcurrentVisits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CapacityLockKey})", cancellationToken);

        var existing = await dbContext.Visits.SingleOrDefaultAsync(x => x.StartOperationId == visit.StartOperationId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new VisitCapacityClaim(true, existing, true);
        }

        var occupied = await dbContext.Visits.CountAsync(x => x.Status != VisitStatus.Completed && x.Status != VisitStatus.Cancelled, cancellationToken);
        if (occupied >= maxConcurrentVisits)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new VisitCapacityClaim(false, null, false);
        }

        dbContext.Visits.Add(visit);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VisitCapacityClaim(true, visit, false);
    }
}
