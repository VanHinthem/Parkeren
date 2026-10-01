namespace Parkeren.Domain.Vehicles;

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
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string LicensePlate { get; private set; } = string.Empty;
    public string NormalizedLicensePlate { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public static string NormalizeLicensePlate(string plate) =>
        new(plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
