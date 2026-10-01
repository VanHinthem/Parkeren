using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitParkingZoneLocationResolver(
    ParkerenDbContext dbContext,
    IServiceProvider serviceProvider)
{
    public async Task<string?> ResolveAsync(
        Guid? parkingZoneId,
        CancellationToken cancellationToken = default)
    {
        if (parkingZoneId.HasValue)
        {
            return await dbContext.ParkingZones.AsNoTracking()
                .Where(x => x.Id == parkingZoneId.Value)
                .Select(x => x.ProviderLocation)
                .SingleOrDefaultAsync(cancellationToken);
        }

        // Legacy Visits created before zone persistence keep working when a provider is available.
        var provider = serviceProvider.GetService(typeof(IParkingProvider)) as IParkingProvider;
        if (provider is null)
            return null;

        var product = await provider.GetProductAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(product.Location) ? null : product.Location;
    }
}
