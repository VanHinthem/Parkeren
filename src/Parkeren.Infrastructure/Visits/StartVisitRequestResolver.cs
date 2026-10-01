using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class StartVisitRequestResolver(
    ParkerenDbContext dbContext,
    IProviderProductCatalogService productCatalog) : IStartVisitRequestResolver
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

        StartVisitProviderContext? providerContext = null;
        try
        {
            var product = await productCatalog.ResolveDefaultForStartAsync(cancellationToken);
            providerContext = new StartVisitProviderContext(
                vehicle.LicensePlate,
                product.Location,
                product.Id,
                product.ProviderProductId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            // The API boundary turns missing/unavailable provider product context into 503.
        }

        return new StartVisitRequestContext(startContext, providerContext);
    }
}
