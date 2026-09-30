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

    [Fact]
    public void Budget_warning_is_reached_exactly_at_threshold()
    {
        var usage = CreateUsage(100, 80);

        var reached = ParkingBudgetWarningEvaluator.GetNewlyReachedThresholds(
            usage, new[] { 80, 90, 100 }, Array.Empty<int>());

        Assert.Equal(new[] { 80 }, reached);
    }

    [Fact]
    public void Budget_warning_returns_all_new_thresholds_crossed_in_one_step()
    {
        var usage = CreateUsage(100, 92);

        var reached = ParkingBudgetWarningEvaluator.GetNewlyReachedThresholds(
            usage, new[] { 80, 90, 100 }, Array.Empty<int>());

        Assert.Equal(new[] { 80, 90 }, reached);
    }

    [Fact]
    public void Budget_warning_ignores_already_notified_thresholds()
    {
        var usage = CreateUsage(100, 92);

        var reached = ParkingBudgetWarningEvaluator.GetNewlyReachedThresholds(
            usage, new[] { 80, 90, 100 }, new[] { 80 });

        Assert.Equal(new[] { 90 }, reached);
    }

    [Fact]
    public void Budget_warning_returns_none_for_zero_budget()
    {
        var usage = CreateUsage(0, 0);

        var reached = ParkingBudgetWarningEvaluator.GetNewlyReachedThresholds(
            usage, new[] { 80, 90, 100 }, Array.Empty<int>());

        Assert.Empty(reached);
    }

    private static ParkingBudgetUsage CreateUsage(double maximumHours, double usedHours)
    {
        var period = new ParkingBudgetPeriod(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(maximumHours));

        return ParkingBudgetCalculator.Calculate(period, TimeSpan.FromHours(usedHours));
    }
}

