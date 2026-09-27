using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingRuleSetPeriodSegmenterTests
{
    private static ParkingRuleSet Rules(DateTimeOffset from, DateTimeOffset? until = null) =>
        new(Guid.NewGuid(), from, until, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());

    [Fact]
    public void Visit_crossing_rule_set_boundary_is_split_at_boundary()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldRules = Rules(DateTimeOffset.MinValue, boundary);
        var newRules = Rules(boundary);
        var start = boundary.AddHours(-2);
        var end = boundary.AddHours(3);

        var periods = ParkingRuleSetPeriodSegmenter.Segment(
            start, end, new[] { oldRules, newRules });

        Assert.Equal(2, periods.Count);
        Assert.Equal((start, boundary, oldRules),
            (periods[0].Start, periods[0].End, periods[0].RuleSet));
        Assert.Equal((boundary, end, newRules),
            (periods[1].Start, periods[1].End, periods[1].RuleSet));
    }

    [Fact]
    public void Gap_between_rule_sets_is_rejected_when_visit_reaches_gap()
    {
        var firstEnd = new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.Zero);
        var secondStart = firstEnd.AddHours(1);
        var first = Rules(DateTimeOffset.MinValue, firstEnd);
        var second = Rules(secondStart);

        Assert.Throws<InvalidOperationException>(() =>
            ParkingRuleSetPeriodSegmenter.Segment(
                firstEnd.AddHours(-1),
                secondStart.AddHours(1),
                new[] { first, second }));
    }
}
