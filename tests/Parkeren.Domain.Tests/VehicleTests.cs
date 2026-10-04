using Parkeren.Domain.Vehicles;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class VehicleTests
{
    [Fact]
    public void License_plate_is_canonical_for_storage_and_provider_use()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), " ab-12-cd ", "AB12CD", null);

        Assert.Equal("AB12CD", vehicle.LicensePlate);
        Assert.Equal("AB12CD", vehicle.NormalizedLicensePlate);
    }

    [Fact]
    public void Conflicting_plate_values_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Vehicle(Guid.NewGuid(), "AB-12-CD", "XY12CD", null));
    }

    [Fact]
    public void Archived_vehicle_cannot_be_reactivated()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "AB-12-CD", "AB12CD", null);

        vehicle.Archive();

        Assert.Equal(VehicleStatus.Archived, vehicle.Status);
        Assert.False(vehicle.IsActive);
        Assert.Throws<InvalidOperationException>(vehicle.Activate);
    }
}
