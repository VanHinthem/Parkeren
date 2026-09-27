namespace Parkeren.Domain.Vehicles;

public sealed class UserVehicle
{
    private UserVehicle() { }

    public UserVehicle(Guid userId, Guid vehicleId)
    {
        UserId = userId;
        VehicleId = vehicleId;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public Guid VehicleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
}
