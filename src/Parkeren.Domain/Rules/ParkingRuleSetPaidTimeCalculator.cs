namespace Parkeren.Domain.Rules;

public static class ParkingRuleSetPaidTimeCalculator
{
    public static TimeSpan Calculate(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        if (end <= start)
            return TimeSpan.Zero;

        ArgumentNullException.ThrowIfNull(ruleSets);

        return ParkingRuleSetPeriodSegmenter
            .Segment(start, end, ruleSets)
            .SelectMany(period =>
                ParkingTimeSegmenter.Segment(period.Start, period.End, period.RuleSet))
            .Where(segment => segment.IsPaid)
            .Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));
    }
}
