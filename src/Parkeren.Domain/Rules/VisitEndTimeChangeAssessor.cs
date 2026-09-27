using Parkeren.Domain.Policies;

namespace Parkeren.Domain.Rules;

public sealed record VisitEndTimeChangeAssessment(
    ParkingUsage Usage,
    ParkingPolicyValidationResult PaidDuration,
    VisitDurationValidationResult ElapsedDuration)
{
    public bool IsAllowed => PaidDuration.IsAllowed && ElapsedDuration.IsAllowed;
}

public static class VisitEndTimeChangeAssessor
{
    public static VisitEndTimeChangeAssessment Assess(
        DateTimeOffset visitStartedAt,
        DateTimeOffset requestedEndAt,
        IEnumerable<ParkingRuleSet> ruleSets,
        EffectiveParkingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);
        ArgumentNullException.ThrowIfNull(policy);

        var segments = ParkingRuleSetPeriodSegmenter.Segment(visitStartedAt, requestedEndAt, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .ToArray();
        var usage = ParkingUsageCalculator.Calculate(segments);
        var paidDuration = ParkingPolicyValidator.ValidatePaidDuration(
            policy,
            TimeSpan.Zero,
            usage.PaidDuration);
        var elapsedDuration = VisitDurationPolicyValidator.Validate(
            policy,
            visitStartedAt,
            requestedEndAt);

        return new VisitEndTimeChangeAssessment(usage, paidDuration, elapsedDuration);
    }
}
