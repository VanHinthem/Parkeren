using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingBudgetCalculatorTests
{
    [Fact]
    public void Remaining_budget_is_based_on_paid_duration()
    {
        var period = new ParkingBudgetPeriod(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(1500));

        var usage = ParkingBudgetCalculator.Calculate(period, TimeSpan.FromHours(125));

        Assert.Equal(TimeSpan.FromHours(1375), usage.RemainingPaidDuration);
    }

    [Fact]
    public void Remaining_budget_never_becomes_negative()
    {
        var period = new ParkingBudgetPeriod(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(100));

        Assert.Equal(TimeSpan.Zero,
            ParkingBudgetCalculator.Calculate(period, TimeSpan.FromHours(110)).RemainingPaidDuration);
    }
}
