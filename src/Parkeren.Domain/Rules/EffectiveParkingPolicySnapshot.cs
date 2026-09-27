using Parkeren.Domain.Policies;

namespace Parkeren.Domain.Rules;

public sealed record EffectiveParkingPolicySnapshot(
    TimeSpan MaxPaidParkingDuration,
    TimeSpan? MaxVisitElapsedDuration,
    bool AllowAutoExtension,
    bool AllowManualStop = true)
{
    public static EffectiveParkingPolicySnapshot Capture(EffectiveParkingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy.MaxPaidParkingDuration, policy.MaxVisitElapsedDuration, policy.AllowAutoExtension, policy.AllowManualStop);
    }

    public EffectiveParkingPolicy ToEffectivePolicy() =>
        new(MaxPaidParkingDuration, MaxVisitElapsedDuration, AllowAutoExtension, AllowManualStop);
}
