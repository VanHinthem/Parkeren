using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class StartVisitRequestResolver(
    ParkerenDbContext dbContext,
    IConfiguration configuration) : IStartVisitRequestResolver
{
    public async Task<StartVisitRequestContext?> ResolveAsync(
        Guid actorUserId,
        Guid ownerUserId,
        Guid vehicleId,
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

        var location = configuration["ParkingProvider:Location"];
        var providerContext = string.IsNullOrWhiteSpace(location)
            ? null
            : new StartVisitProviderContext(vehicle.LicensePlate, location);

        return new StartVisitRequestContext(startContext, providerContext);
    }
}
