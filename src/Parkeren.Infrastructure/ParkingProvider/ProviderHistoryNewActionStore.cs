using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class ProviderHistoryNewActionStore(ParkerenDbContext dbContext)
    : IProviderHistoryNewActionStore
{
    public async Task<ProviderHistoryNewActionResult> InsertIfMissingAsync(
        string providerProductId,
        ProviderActionHistoryRecord record,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProviderActionId))
            throw new ArgumentException("Provider action id is required.", nameof(record));
        if (!string.Equals(record.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only completed history actions can be imported.");

        var id = record.ProviderActionId.Trim();
        var productId = providerProductId.Trim();

        var existing = await dbContext.ProviderParkingActions
            .AsNoTracking()
            .Where(x => x.ProviderActionId == id)
            .Select(x => new { x.ProviderProductId })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ProviderProductId, productId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Provider action {id} belongs to another product.");
            return ProviderHistoryNewActionResult.AlreadyExists;
        }

        var plate = string.IsNullOrWhiteSpace(record.LicensePlate)
            ? null : Vehicle.NormalizeLicensePlate(record.LicensePlate);
        if (string.IsNullOrEmpty(plate))
            throw new InvalidOperationException($"Provider history action {id} has no usable license plate.");

        var vehicle = await dbContext.Vehicles.SingleOrDefaultAsync(
            x => x.NormalizedLicensePlate == plate, cancellationToken);
        if (vehicle is null)
        {
            vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
            dbContext.Vehicles.Add(vehicle);
        }

        // A plate is not proof of a driver. Inference is made once at insertion,
        // from current distinct explicit user-vehicle links, not on later syncs.
        var userIds = await dbContext.UserVehicles.AsNoTracking()
            .Where(x => x.VehicleId == vehicle.Id)
            .Select(x => x.UserId)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);
        var assignment = ProviderActionAssignment.InferFromVehicleUsers(userIds);

        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), id, productId, record.Location, vehicle.Id, assignment,
            record.ActualStartAt, record.ActualEndAt, record.ProviderCostAmount,
            record.Status, observedAt);

        dbContext.ProviderParkingActions.Add(action);
        // Database unique indexes remain authoritative under concurrent imports:
        // a racing writer fails safely rather than creating duplicate actions.
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProviderHistoryNewActionResult.Inserted;
    }
}
