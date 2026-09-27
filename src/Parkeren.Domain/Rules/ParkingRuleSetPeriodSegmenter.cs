namespace Parkeren.Domain.Rules;

public sealed record ParkingRuleSetPeriod(
    DateTimeOffset Start,
    DateTimeOffset End,
    ParkingRuleSet RuleSet);

public static class ParkingRuleSetPeriodSegmenter
{
    public static IReadOnlyList<ParkingRuleSetPeriod> Segment(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        if (end <= start) throw new ArgumentException("End must be after start.", nameof(end));
        ArgumentNullException.ThrowIfNull(ruleSets);

        var sets = ruleSets.ToArray();
        ParkingRuleSetResolver.ValidateNoOverlap(sets);

        var boundaries = sets
            .SelectMany(x => new DateTimeOffset?[] { x.ValidFrom, x.ValidUntil })
            .Where(x => x.HasValue && x.Value > start && x.Value < end)
            .Select(x => x!.Value)
            .Append(start)
            .Append(end)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var periods = new List<ParkingRuleSetPeriod>();
        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var periodStart = boundaries[i];
            var periodEnd = boundaries[i + 1];
            var ruleSet = ParkingRuleSetResolver.Resolve(sets, periodStart);
            periods.Add(new ParkingRuleSetPeriod(periodStart, periodEnd, ruleSet));
        }

        return periods;
    }
}
