using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingUsageCalculatorTests
{
    [Fact]
    public void Free_time_does_not_count_as_paid_usage()
    {
        var start = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);
        var segments = new[]
        {
            new ParkingTimeSegment(start, start.AddHours(2), true),
            new ParkingTimeSegment(start.AddHours(2), start.AddHours(14), false),
            new ParkingTimeSegment(start.AddHours(14), start.AddHours(15.5), true)
        };

        var usage = ParkingUsageCalculator.Calculate(segments);

        Assert.Equal(TimeSpan.FromHours(3.5), usage.PaidDuration);
        Assert.Equal(3.5m, usage.PaidHours);
    }

    [Fact]
    public void Cost_is_based_only_on_paid_hours()
    {
        var usage = new ParkingUsage(TimeSpan.FromMinutes(90));

        var cost = ParkingCostCalculator.Calculate(usage, 2.50m);

        Assert.Equal(3.75m, cost.Amount);
    }

    [Fact]
    public void Negative_hourly_rate_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ParkingCostCalculator.Calculate(new ParkingUsage(TimeSpan.FromHours(1)), -1m));
    }
}
