namespace Parkeren.Domain.Policies;
public sealed class DefaultParkingPolicy
{
    private DefaultParkingPolicy() { }
    public DefaultParkingPolicy(Guid id, TimeSpan maxPaidParkingDuration, TimeSpan? maxVisitElapsedDuration, bool allowAutoExtension, bool allowManualStop = true)
    { Id=id; MaxPaidParkingDuration=maxPaidParkingDuration; MaxVisitElapsedDuration=maxVisitElapsedDuration; AllowAutoExtension=allowAutoExtension; AllowManualStop=allowManualStop; UpdatedAt=DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public TimeSpan MaxPaidParkingDuration { get; private set; }
    public TimeSpan? MaxVisitElapsedDuration { get; private set; }
    public bool AllowAutoExtension { get; private set; }
    public bool AllowManualStop { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
public sealed class UserPolicyOverride
{
    private UserPolicyOverride() { }
    public UserPolicyOverride(Guid userId){UserId=userId;UpdatedAt=DateTimeOffset.UtcNow;}
    public Guid UserId { get; private set; }
    public TimeSpan? MaxPaidParkingDuration { get; private set; }
    public TimeSpan? MaxVisitElapsedDuration { get; private set; }
    public bool? AllowAutoExtension { get; private set; }
    public bool? AllowManualStop { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
public sealed record EffectiveParkingPolicy(TimeSpan MaxPaidParkingDuration, TimeSpan? MaxVisitElapsedDuration, bool AllowAutoExtension, bool AllowManualStop = true);
public static class ParkingPolicyResolver
{
    public static EffectiveParkingPolicy Resolve(DefaultParkingPolicy defaults, UserPolicyOverride? overrides) => new(
        overrides?.MaxPaidParkingDuration ?? defaults.MaxPaidParkingDuration,
        overrides?.MaxVisitElapsedDuration ?? defaults.MaxVisitElapsedDuration,
        overrides?.AllowAutoExtension ?? defaults.AllowAutoExtension,
        overrides?.AllowManualStop ?? defaults.AllowManualStop);
}
