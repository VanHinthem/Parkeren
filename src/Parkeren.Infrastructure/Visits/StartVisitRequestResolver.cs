using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Zones;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class StartVisitRequestResolver(
    ParkerenDbContext dbContext) : IStartVisitRequestResolver
{
    public async Task<StartVisitRequestContext?> ResolveAsync(
        Guid actorUserId,
        Guid ownerUserId,
        Guid vehicleId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default)
    {
        var actor = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == actorUserId, cancellationToken);
        var owner = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ownerUserId, cancellationToken);
        var vehicle = await dbContext.Vehicles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == vehicleId, cancellationToken);

        if (actor is null || owner is null || vehicle is null)
            return null;

        var isAssigned = await dbContext.UserVehicles
            .AsNoTracking()
            .AnyAsync(x => x.UserId == ownerUserId && x.VehicleId == vehicleId, cancellationToken);

        var startContext = new StartVisitContext(
            new StartVisitActor(actor.Id, actor.Role, actor.IsActive),
            new StartVisitOwner(owner.Id, owner.IsActive),
            new StartVisitVehicle(vehicle.Id, vehicle.IsActive, isAssigned));

        var zones = await dbContext.ParkingZones.AsNoTracking()
            .Where(x => x.IsDefault &&
                        x.ValidFrom <= startAt &&
                        (!x.ValidUntil.HasValue || startAt < x.ValidUntil.Value))
            .ToListAsync(cancellationToken);

        StartVisitProviderContext? providerContext = null;
        if (zones.Count > 0)
        {
            var zone = ParkingZoneResolver.ResolveDefault(zones, startAt);
            providerContext = new StartVisitProviderContext(
                vehicle.LicensePlate,
                zone.ProviderLocation,
                zone.Id);
        }

        return new StartVisitRequestContext(startContext, providerContext);
    }
}
