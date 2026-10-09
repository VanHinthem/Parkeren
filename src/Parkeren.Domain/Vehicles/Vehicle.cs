namespace Parkeren.Domain.Vehicles;

public enum VehicleStatus
{
    Active,
    Inactive,
    Archived
}

public sealed class Vehicle
{
    private Vehicle() { }

    public Vehicle(Guid id, string licensePlate, string normalizedLicensePlate, string? displayName)
    {
        var canonicalPlate = NormalizeLicensePlate(licensePlate);
        if (canonicalPlate != NormalizeLicensePlate(normalizedLicensePlate))
            throw new ArgumentException("License plate values must match.", nameof(normalizedLicensePlate));

        Id = id;
        LicensePlate = canonicalPlate;
        NormalizedLicensePlate = canonicalPlate;
        DisplayName = displayName;
        Status = VehicleStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Registers an otherwise unknown plate observed in provider history.
    /// History alone does not authorize parking or create a UserVehicle assignment.
    /// </summary>
    public static Vehicle FromProviderHistory(Guid id, string licensePlate)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Vehicle id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(licensePlate))
            throw new ArgumentException("License plate is required.", nameof(licensePlate));

        var normalizedPlate = NormalizeLicensePlate(licensePlate);
        if (string.IsNullOrEmpty(normalizedPlate))
            throw new ArgumentException("License plate must contain letters or digits.", nameof(licensePlate));

        var vehicle = new Vehicle(id, normalizedPlate, normalizedPlate, null);
        vehicle.Deactivate();
        return vehicle;
    }

    public Guid Id { get; private set; }
    public string LicensePlate { get; private set; } = string.Empty;
    public string NormalizedLicensePlate { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public VehicleStatus Status { get; private set; }
    public bool IsActive => Status == VehicleStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }

    public void Activate()
    {
        if (Status == VehicleStatus.Archived)
            throw new InvalidOperationException("Archived vehicles cannot be activated.");

        Status = VehicleStatus.Active;
    }

    public void Deactivate()
    {
        if (Status != VehicleStatus.Archived)
            Status = VehicleStatus.Inactive;
    }

    public void Archive() => Status = VehicleStatus.Archived;

    public static string NormalizeLicensePlate(string plate) =>
        new(plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
