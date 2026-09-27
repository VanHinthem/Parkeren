namespace Parkeren.Domain.Policies;

public sealed record ParkingPolicyValidationResult(bool IsAllowed, TimeSpan RemainingPaidDuration);

public static class ParkingPolicyValidator
{
    public static ParkingPolicyValidationResult ValidatePaidDuration(
        EffectiveParkingPolicy policy,
        TimeSpan alreadyUsedPaidDuration,
        TimeSpan requestedPaidDuration)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (alreadyUsedPaidDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(alreadyUsedPaidDuration));
        if (requestedPaidDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestedPaidDuration));

        var remaining = policy.MaxPaidParkingDuration - alreadyUsedPaidDuration;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        return new ParkingPolicyValidationResult(requestedPaidDuration <= remaining, remaining);
    }
}
