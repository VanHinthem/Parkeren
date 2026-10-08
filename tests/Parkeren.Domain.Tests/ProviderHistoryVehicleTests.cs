using Parkeren.Domain.Vehicles;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderHistoryVehicleTests
{
    [Fact]
    public void Historical_vehicle_is_inactive_and_canonical()
    {
        var id = Guid.NewGuid();

        var vehicle = Vehicle.FromProviderHistory(id, "ab-12-cd");

        Assert.Equal(id, vehicle.Id);
        Assert.Equal("AB12CD", vehicle.LicensePlate);
        Assert.Equal("AB12CD", vehicle.NormalizedLicensePlate);
        Assert.Equal(VehicleStatus.Inactive, vehicle.Status);
        Assert.False(vehicle.IsActive);
        Assert.Null(vehicle.DisplayName);
    }

    [Fact]
    public void Historical_vehicle_does_not_create_a_user_assignment()
    {
        // UserVehicle is a separate join entity, created only by explicit user management.
        var vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), "XY-12-ZZ");

        Assert.False(vehicle.IsActive);
        Assert.Equal("XY12ZZ", vehicle.NormalizedLicensePlate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("---")]
    public void Invalid_history_plate_is_rejected(string plate)
    {
        Assert.Throws<ArgumentException>(() => Vehicle.FromProviderHistory(Guid.NewGuid(), plate));
    }

    [Fact]
    public void Empty_vehicle_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Vehicle.FromProviderHistory(Guid.Empty, "AB12CD"));
    }
}
