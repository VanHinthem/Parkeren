using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitCapacityClaimer(ParkerenDbContext dbContext) : IVisitCapacityClaimer
{
    private const long CapacityLockKey = 0x5041524B; // PARK

    public async Task<VisitCapacityClaim> TryClaimAsync(Visit visit, int maxGlobalConcurrentVisits, int maxUserConcurrentVisits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (maxGlobalConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxGlobalConcurrentVisits));
        if (maxUserConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxUserConcurrentVisits));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CapacityLockKey})", cancellationToken);

        var existing = await dbContext.Visits.SingleOrDefaultAsync(x => x.StartOperationId == visit.StartOperationId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new VisitCapacityClaim(true, existing, true);
        }

        // Administration uses the same lock. Recheck persisted limits after taking it,
        // since the operational context may have been resolved before an admin update.
        var globalLimit = await dbContext.ParkingSystemSettings.AsNoTracking()
            .Select(x => x.MaxConcurrentVisits).SingleAsync(cancellationToken);
        var userOverride = await dbContext.UserPolicyOverrides.AsNoTracking()
            .Where(x => x.UserId == visit.UserId)
            .Select(x => x.MaxConcurrentVisits)
            .SingleOrDefaultAsync(cancellationToken);
        maxGlobalConcurrentVisits = Math.Min(maxGlobalConcurrentVisits, globalLimit);
        if (userOverride is { } userLimit)
            maxUserConcurrentVisits = Math.Min(maxUserConcurrentVisits, userLimit);

        var occupiedByUser = await dbContext.Visits.CountAsync(x => x.UserId == visit.UserId && x.Status != VisitStatus.Completed && x.Status != VisitStatus.Cancelled, cancellationToken);
        if (occupiedByUser >= maxUserConcurrentVisits)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new VisitCapacityClaim(false, null, false);
        }

        var occupied = await dbContext.Visits.CountAsync(x => x.Status != VisitStatus.Completed && x.Status != VisitStatus.Cancelled, cancellationToken);
        if (occupied >= maxGlobalConcurrentVisits)
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
