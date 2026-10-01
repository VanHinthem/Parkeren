namespace Parkeren.Domain.Zones;

public sealed class ParkingZone
{
    private ParkingZone() { }

    public ParkingZone(
        Guid id,
        string name,
        string providerLocation,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        bool isDefault)
    {
        if (id == Guid.Empty) throw new ArgumentException("Zone id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Zone name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(providerLocation)) throw new ArgumentException("Provider location is required.", nameof(providerLocation));
        if (validUntil.HasValue && validUntil.Value <= validFrom) throw new ArgumentException("ValidUntil must be after ValidFrom.", nameof(validUntil));

        Id = id;
        Name = name.Trim();
        ProviderLocation = providerLocation.Trim();
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        IsDefault = isDefault;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ProviderLocation { get; private set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidUntil { get; private set; }
    public bool IsDefault { get; private set; }

    public bool IsValidAt(DateTimeOffset instant) =>
        ValidFrom <= instant && (!ValidUntil.HasValue || instant < ValidUntil.Value);

    public void CloseAt(DateTimeOffset validUntil)
    {
        if (ValidUntil.HasValue)
            throw new InvalidOperationException("Only an open-ended parking zone can be closed.");
        if (validUntil <= ValidFrom)
            throw new ArgumentOutOfRangeException(nameof(validUntil));

        ValidUntil = validUntil;
    }
}

public static class ParkingZoneResolver
{
    public static ParkingZone ResolveDefault(
        IEnumerable<ParkingZone> zones,
        DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(zones);

        var applicable = zones
            .Where(x => x.IsDefault && x.IsValidAt(instant))
            .ToArray();

        return applicable.Length switch
        {
            1 => applicable[0],
            0 => throw new InvalidOperationException($"No default parking zone applies at {instant:O}."),
            _ => throw new InvalidOperationException($"Multiple default parking zones apply at {instant:O}.")
        };
    }

    public static void ValidateDefaultNoOverlap(IEnumerable<ParkingZone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);

        var ordered = zones
            .Where(x => x.IsDefault)
            .OrderBy(x => x.ValidFrom)
            .ToArray();

        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            if (!previous.ValidUntil.HasValue || current.ValidFrom < previous.ValidUntil.Value)
                throw new InvalidOperationException("Default parking zones may not overlap.");
        }
    }
}
