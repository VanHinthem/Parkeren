using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class PlannedVisitAssessorTests
{
    [Fact]
    public void Assessment_combines_time_segmentation_and_policy_limits()
    {
        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.MinValue,
            null,
            TimeSpan.FromHours(4),
            new[] { new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)) });
        var policy = new EffectiveParkingPolicy(
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(16),
            true);

        // Monday 18:00 UTC = 20:00 CEST, through Tuesday 08:00 UTC = 10:00 CEST.
        // Only Tuesday 09:00-10:00 local would be paid, but Tuesday has no paid window here.
        var start = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

        var result = PlannedVisitAssessor.Assess(start, end, rules, policy, TimeSpan.FromHours(3));

        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.Zero, result.Usage.PaidDuration);
        Assert.Equal(TimeSpan.FromHours(1), result.PaidDuration.RemainingPaidDuration);
    }

    [Fact]
    public void Assessment_rejects_when_paid_duration_exceeds_remaining_policy()
    {
        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.MinValue,
            null,
            TimeSpan.FromHours(4),
            new[] { new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)) });
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true);
        var start = new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var result = PlannedVisitAssessor.Assess(start, end, rules, policy, TimeSpan.FromHours(2));

        Assert.False(result.IsAllowed);
        Assert.Equal(TimeSpan.FromHours(4), result.Usage.PaidDuration);
        Assert.Equal(TimeSpan.FromHours(2), result.PaidDuration.RemainingPaidDuration);
    }
}
