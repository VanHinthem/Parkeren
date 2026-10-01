using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
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
    [Fact]
    public void Realized_budget_usage_counts_only_paid_time()
    {
        var start = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero); // 17:00 Europe/Amsterdam
        var visit = CompletedVisit(start, start.AddHours(5)); // through 22:00 Europe/Amsterdam
        var period = BudgetPeriod(start.Date, start.Date.AddDays(1), 100);
        var rules = Rules(start.AddDays(-1));

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, new[] { rules });

        Assert.Equal(TimeSpan.FromHours(3), usage.UsedPaidDuration);
    }

    [Fact]
    public void Realized_budget_usage_counts_only_part_inside_budget_period()
    {
        var periodStart = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        var periodEnd = periodStart.AddHours(2);
        var visit = CompletedVisit(periodStart.AddHours(-1), periodEnd.AddHours(1));
        var period = BudgetPeriod(periodStart, periodEnd, 100);
        var rules = Rules(periodStart.AddDays(-1));

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, new[] { rules });

        Assert.Equal(TimeSpan.FromHours(2), usage.UsedPaidDuration);
    }

    private static Visit CompletedVisit(DateTimeOffset start, DateTimeOffset end)
    {
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            start, end,
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(8), null, true)));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(end);
        return visit;
    }

    private static ParkingBudgetPeriod BudgetPeriod(DateTimeOffset start, DateTimeOffset end, double maximumHours) =>
        new(Guid.NewGuid(), start, end, TimeSpan.FromHours(maximumHours));

    private static ParkingRuleSet Rules(DateTimeOffset validFrom) =>
        new(
            Guid.NewGuid(),
            validFrom,
            null,
            TimeSpan.FromHours(4),
            new[]
            {
                new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                new PaidWindow(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                new PaidWindow(DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                new PaidWindow(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                new PaidWindow(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                new PaidWindow(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(20, 0))
            });

}

