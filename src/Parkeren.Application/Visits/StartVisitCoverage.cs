using Parkeren.Domain.Rules;

namespace Parkeren.Application.Visits;

public static class StartVisitCoverage
{
    public static bool RequiresProviderCoverageNow(
        DateTimeOffset startAt,
        DateTimeOffset evaluationEndAt,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);
        if (evaluationEndAt <= startAt)
            throw new ArgumentOutOfRangeException(nameof(evaluationEndAt));

        var first = ParkingRuleSetPeriodSegmenter.Segment(startAt, evaluationEndAt, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .OrderBy(x => x.Start)
            .FirstOrDefault();

        if (first is null || first.Start != startAt)
            throw new InvalidOperationException("No parking rule covers the Visit start time.");

        return first.IsPaid;
    }
}
