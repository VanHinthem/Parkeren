using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingRuleSetPaidTimeCalculatorTests
{
    [Fact]
    public void Calculates_paid_time_across_rule_set_boundary()
    {
        var monday = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var boundary = monday.AddHours(4);
        var end = monday.AddHours(8);

        var first = new ParkingRuleSet(
            Guid.NewGuid(),
            monday,
            boundary,
            TimeSpan.FromHours(4),
            [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0))]);

        var second = new ParkingRuleSet(
            Guid.NewGuid(),
            boundary,
            null,
            TimeSpan.FromHours(4),
            [new PaidWindow(DayOfWeek.Monday, new TimeOnly(14, 0), new TimeOnly(16, 0))]);

        var paid = ParkingRuleSetPaidTimeCalculator.Calculate(monday, end, [first, second]);

        Assert.Equal(TimeSpan.FromHours(4), paid);
    }

    [Fact]
    public void Returns_zero_when_end_does_not_follow_start()
    {
        var instant = DateTimeOffset.UtcNow;

        Assert.Equal(
            TimeSpan.Zero,
            ParkingRuleSetPaidTimeCalculator.Calculate(instant, instant, []));
    }
}
