using Parkeren.Domain.Policies;

namespace Parkeren.Domain.Rules;

public sealed record EffectiveParkingPolicySnapshot(
    TimeSpan MaxPaidParkingDuration,
    TimeSpan? MaxVisitElapsedDuration,
    bool AllowAutoExtension)
{
    public static EffectiveParkingPolicySnapshot Capture(EffectiveParkingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy.MaxPaidParkingDuration, policy.MaxVisitElapsedDuration, policy.AllowAutoExtension);
    }

    public EffectiveParkingPolicy ToEffectivePolicy() =>
        new(MaxPaidParkingDuration, MaxVisitElapsedDuration, AllowAutoExtension);
}
