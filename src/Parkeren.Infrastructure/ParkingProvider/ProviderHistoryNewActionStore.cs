using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.ParkingProvider;
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
        Guid? vehicleId = null;
        var assignment = ProviderActionAssignment.Unassigned;
        if (!string.IsNullOrEmpty(plate))
        {
            var vehicle = await dbContext.Vehicles.SingleOrDefaultAsync(
                x => x.NormalizedLicensePlate == plate, cancellationToken);
            if (vehicle is null)
            {
                vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
                dbContext.Vehicles.Add(vehicle);
            }

            vehicleId = vehicle.Id;
            // Infer a user only when the history action has an identifiable vehicle.
            var userIds = await dbContext.UserVehicles.AsNoTracking()
                .Where(x => x.VehicleId == vehicle.Id)
                .Select(x => x.UserId)
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken);
            assignment = ProviderActionAssignment.InferFromVehicleUsers(userIds);
        }

        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), id, productId, record.Location, vehicleId, assignment,
            record.ActualStartAt, record.ActualEndAt, record.ProviderCostAmount,
            record.Status, observedAt);

        dbContext.ProviderParkingActions.Add(action);
        // Database unique indexes remain authoritative under concurrent imports:
        // a racing writer fails safely rather than creating duplicate actions.
        await dbContext.SaveChangesAsync(cancellationToken);

        // Once an external provider action has been safely imported, the external
        // discrepancy is no longer actionable. Keep this in the page transaction.
        var localProductId = await dbContext.ParkingProviderProducts.AsNoTracking()
            .Where(x => x.ProviderProductId == productId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (localProductId is Guid localId)
        {
            var discrepancyKey = $"external-provider-action:{localId:N}:{id}";
            var discrepancy = await dbContext.ProviderDiscrepancies
                .SingleOrDefaultAsync(x => x.Key == discrepancyKey &&
                    x.Type == ProviderDiscrepancyType.ExternalProviderAction &&
                    x.Status == ProviderDiscrepancyStatus.Open, cancellationToken);
            if (discrepancy is not null)
            {
                discrepancy.Resolve(observedAt >= discrepancy.LastObservedAt
                    ? observedAt : discrepancy.LastObservedAt);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        return ProviderHistoryNewActionResult.Inserted;
    }
}
