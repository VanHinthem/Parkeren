namespace Parkeren.Domain.Policies;

public sealed record VisitDurationValidationResult(
    bool IsAllowed,
    TimeSpan? RemainingElapsedDuration);

public static class VisitDurationPolicyValidator
{
    public static VisitDurationValidationResult Validate(
        EffectiveParkingPolicy policy,
        DateTimeOffset visitStartedAt,
        DateTimeOffset requestedEndAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (requestedEndAt < visitStartedAt)
            throw new ArgumentOutOfRangeException(nameof(requestedEndAt));

        if (!policy.MaxVisitElapsedDuration.HasValue)
            return new VisitDurationValidationResult(true, null);

        var elapsed = requestedEndAt - visitStartedAt;
        var remaining = policy.MaxVisitElapsedDuration.Value - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        return new VisitDurationValidationResult(
            elapsed <= policy.MaxVisitElapsedDuration.Value,
            remaining);
    }
}
