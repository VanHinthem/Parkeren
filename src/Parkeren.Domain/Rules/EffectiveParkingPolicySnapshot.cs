using Parkeren.Domain.Policies;

namespace Parkeren.Domain.Rules;

public sealed record EffectiveParkingPolicySnapshot(
    TimeSpan? MaxPaidParkingDuration,
    TimeSpan? MaxVisitElapsedDuration,
    bool AllowVisitExtension,
    bool AllowOpenEndedVisits = true)
{
    public static EffectiveParkingPolicySnapshot Capture(EffectiveParkingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy.MaxPaidParkingDuration, policy.MaxVisitElapsedDuration, policy.AllowVisitExtension, policy.AllowOpenEndedVisits);
    }

    public EffectiveParkingPolicy ToEffectivePolicy() =>
        new(MaxPaidParkingDuration, MaxVisitElapsedDuration, AllowVisitExtension, AllowOpenEndedVisits);
}
