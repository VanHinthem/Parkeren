using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitCapacityClaimer(
    ParkerenDbContext dbContext,
    ExternalActiveProviderActionCounter externalActions) : IVisitCapacityClaimer
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

        var activeOwner = await dbContext.Users.AnyAsync(
            x => x.Id == visit.UserId && x.Status == Domain.Users.UserStatus.Active,
            cancellationToken);
        var activeActor = await dbContext.Users.AnyAsync(
            x => x.Id == visit.StartedByUserId && x.Status == Domain.Users.UserStatus.Active,
            cancellationToken);
        var activeVehicle = await dbContext.Vehicles.AnyAsync(
            x => x.Id == visit.VehicleId && x.Status == Domain.Vehicles.VehicleStatus.Active,
            cancellationToken);
        if (!activeOwner || !activeActor || !activeVehicle)
            throw new InvalidOperationException("Gebruiker of voertuig is niet langer actief; de Visit is niet gestart.");

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
        // Count only confirmed external actions that are not already represented
        // by a managed action or Visit; user-specific limits remain Visit-based.
        var occupiedExternally = await externalActions.CountAsync(cancellationToken);
        if (occupied + occupiedExternally >= maxGlobalConcurrentVisits)
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
