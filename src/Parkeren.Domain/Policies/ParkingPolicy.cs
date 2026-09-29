namespace Parkeren.Domain.Policies;
public sealed class ParkingSystemSettings
{
    private ParkingSystemSettings() { }
    public ParkingSystemSettings(Guid id, int maxConcurrentVisits)
    {
        if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits));
        Id = id;
        MaxConcurrentVisits = maxConcurrentVisits;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public int MaxConcurrentVisits { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void SetMaxConcurrentVisits(int maxConcurrentVisits)
    {
        if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits));
        MaxConcurrentVisits = maxConcurrentVisits;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class DefaultParkingPolicy
{
    private DefaultParkingPolicy() { }
    public DefaultParkingPolicy(Guid id, TimeSpan? maxPaidParkingDuration, TimeSpan? maxVisitElapsedDuration, bool allowVisitExtension, bool allowOpenEndedVisits = true, int maxConcurrentVisits = 1)
    { if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits)); Id=id; MaxPaidParkingDuration=maxPaidParkingDuration; MaxVisitElapsedDuration=maxVisitElapsedDuration; AllowVisitExtension=allowVisitExtension; AllowOpenEndedVisits=allowOpenEndedVisits; MaxConcurrentVisits=maxConcurrentVisits; UpdatedAt=DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public TimeSpan? MaxPaidParkingDuration { get; private set; }
    public TimeSpan? MaxVisitElapsedDuration { get; private set; }
    public bool AllowVisitExtension { get; private set; }
    public bool AllowOpenEndedVisits { get; private set; }
    public int MaxConcurrentVisits { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
public sealed class UserPolicyOverride
{
    private UserPolicyOverride() { }
    public UserPolicyOverride(Guid userId){UserId=userId;UpdatedAt=DateTimeOffset.UtcNow;}
    public void SetMaxConcurrentVisits(int? maxConcurrentVisits) { if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits)); MaxConcurrentVisits=maxConcurrentVisits; UpdatedAt=DateTimeOffset.UtcNow; }
    public Guid UserId { get; private set; }
    public TimeSpan? MaxPaidParkingDuration { get; private set; }
    public TimeSpan? MaxVisitElapsedDuration { get; private set; }
    public bool? AllowVisitExtension { get; private set; }
    public bool? AllowOpenEndedVisits { get; private set; }
    public int? MaxConcurrentVisits { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
public sealed record EffectiveParkingPolicy(TimeSpan? MaxPaidParkingDuration, TimeSpan? MaxVisitElapsedDuration, bool AllowVisitExtension, bool AllowOpenEndedVisits = true, int MaxConcurrentVisits = 1);
public static class ParkingPolicyResolver
{
    public static EffectiveParkingPolicy Resolve(DefaultParkingPolicy defaults, UserPolicyOverride? overrides) => new(
        overrides?.MaxPaidParkingDuration ?? defaults.MaxPaidParkingDuration,
        overrides?.MaxVisitElapsedDuration ?? defaults.MaxVisitElapsedDuration,
        overrides?.AllowVisitExtension ?? defaults.AllowVisitExtension,
        overrides?.AllowOpenEndedVisits ?? defaults.AllowOpenEndedVisits,
        overrides?.MaxConcurrentVisits ?? defaults.MaxConcurrentVisits);
}
