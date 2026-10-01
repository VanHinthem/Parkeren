using Parkeren.Domain.Policies;

namespace Parkeren.Domain.Rules;

public sealed record PlannedVisitAssessment(
    ParkingUsage Usage,
    ParkingPolicyValidationResult PaidDuration,
    VisitDurationValidationResult ElapsedDuration)
{
    public bool IsAllowed => PaidDuration.IsAllowed && ElapsedDuration.IsAllowed;
}

public static class PlannedVisitAssessor
{
    public static PlannedVisitAssessment Assess(
        DateTimeOffset visitStartedAt,
        DateTimeOffset requestedEndAt,
        ParkingRuleSet rules,
        EffectiveParkingPolicy policy,
        TimeSpan alreadyUsedPaidDuration)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policy);

        var segments = ParkingTimeSegmenter.Segment(visitStartedAt, requestedEndAt, rules);
        var usage = ParkingUsageCalculator.Calculate(segments);
        var paidDuration = ParkingPolicyValidator.ValidatePaidDuration(
            policy, alreadyUsedPaidDuration, usage.PaidDuration);
        var elapsedDuration = VisitDurationPolicyValidator.Validate(
            policy, visitStartedAt, requestedEndAt);

        return new PlannedVisitAssessment(usage, paidDuration, elapsedDuration);
    }
}
