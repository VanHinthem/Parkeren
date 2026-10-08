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
    public void Historical_budget_baseline_marks_reached_thresholds_without_repeating_existing_ones()
    {
        var usage = CreateUsage(100, 95);

        var thresholds = ParkingBudgetWarningEvaluator.GetHistoricalBaselineThresholds(
            usage, [75, 90, 100], [75]);

        Assert.Equal(new[] { 90 }, thresholds);
    }

    [Fact]
    public void Historical_budget_baseline_never_acknowledges_future_thresholds()
    {
        var usage = CreateUsage(100, 70);

        var thresholds = ParkingBudgetWarningEvaluator.GetHistoricalBaselineThresholds(
            usage, [75, 90], []);

        Assert.Empty(thresholds);
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
        var action = CompletedAction(visit.Id, start, start.AddHours(5));

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, new[] { action }, new[] { rules });

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
        var action = CompletedAction(visit.Id, periodStart.AddHours(-1), periodEnd.AddHours(1));

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, new[] { action }, new[] { rules });

        Assert.Equal(TimeSpan.FromHours(2), usage.UsedPaidDuration);
    }

    [Fact]
    public void Realized_budget_usage_excludes_gaps_between_provider_actions()
    {
        var start = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        var visit = CompletedVisit(start, start.AddHours(5));
        var period = BudgetPeriod(start.Date, start.Date.AddDays(1), 100);
        var rules = Rules(start.AddDays(-1));
        var actions = new[]
        {
            CompletedAction(visit.Id, start, start.AddHours(1)),
            CompletedAction(visit.Id, start.AddHours(2), start.AddHours(3))
        };

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, actions, new[] { rules });

        Assert.Equal(TimeSpan.FromHours(2), usage.UsedPaidDuration);
    }

    [Fact]
    public void Realized_budget_usage_counts_pre_start_cancelled_action_as_zero()
    {
        var start = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        var visit = CompletedVisit(start, start.AddHours(5));
        var period = BudgetPeriod(start.Date, start.Date.AddDays(1), 100);
        var rules = Rules(start.AddDays(-1));
        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, start.AddHours(1), start.AddHours(2));
        action.MarkStarting();
        action.MarkScheduled("scheduled-provider-action");
        action.BeginStopping();
        action.MarkStopped(start.AddMinutes(30), "stopped");

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, new[] { action }, new[] { rules });

        Assert.Equal(TimeSpan.Zero, usage.UsedPaidDuration);
    }

    [Fact]
    public void Realized_budget_usage_uses_provider_readback_start_for_scheduled_action()
    {
        var start = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        var visit = CompletedVisit(start, start.AddHours(5));
        var period = BudgetPeriod(start.Date, start.Date.AddDays(1), 100);
        var rules = Rules(start.AddDays(-1));
        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, start.AddHours(1), start.AddHours(3));
        action.MarkStarting();
        action.MarkScheduled("scheduled-provider-action");
        action.BeginStopping();
        action.MarkStopped(start.AddHours(2), "stopped", start.AddHours(1));

        var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, new[] { visit }, new[] { action }, new[] { rules });

        Assert.Equal(start.AddHours(1), action.ActualStartAt);
        Assert.Equal(TimeSpan.FromHours(1), usage.UsedPaidDuration);
    }

    [Fact]
    public void Action_based_budget_includes_imported_without_visit_and_deduplicates_overlap()
    {
        var start = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        var period = BudgetPeriod(start.Date, start.Date.AddDays(1), 100);
        var rules = Rules(start.AddDays(-1));
        var imported = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), "history-1", "product-1", null, Guid.NewGuid(),
            ProviderActionAssignment.Unassigned, start, start.AddHours(2),
            0.5m, "COMPLETED", start.AddHours(3));
        var managed = CompletedAction(Guid.NewGuid(), start.AddHours(1), start.AddHours(3));

        var usage = RealizedParkingBudgetUsageCalculator.CalculateFromActions(
            period, new[] { imported, managed, imported }, new[] { rules });

        Assert.Equal(TimeSpan.FromHours(3), usage.UsedPaidDuration);
    }

    [Fact]
    public void Action_based_budget_ignores_unfinished_actions()
    {
        var start = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        var ongoing = new ProviderParkingAction(
            Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(2));

        var usage = RealizedParkingBudgetUsageCalculator.CalculateFromActions(
            BudgetPeriod(start.Date, start.Date.AddDays(1), 100),
            new[] { ongoing }, new[] { Rules(start.AddDays(-1)) });

        Assert.Equal(TimeSpan.Zero, usage.UsedPaidDuration);
    }

    [Fact]
    public void Action_based_budget_clips_at_local_new_year_boundary()
    {
        // The 2027 Amsterdam year begins at 2026-12-31 23:00 UTC.
        var yearStart = new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var yearEnd = new DateTimeOffset(2027, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var actionStart = yearStart.AddHours(-4);
        var actionEnd = yearStart.AddHours(11);
        var action = CompletedAction(Guid.NewGuid(), actionStart, actionEnd);

        var usage = RealizedParkingBudgetUsageCalculator.CalculateFromActions(
            BudgetPeriod(yearStart, yearEnd, 1500),
            new[] { action }, new[] { Rules(yearStart.AddDays(-2)) });

        // Friday 1 January, 09:00-11:00 local; previous year's time is excluded.
        Assert.Equal(TimeSpan.FromHours(2), usage.UsedPaidDuration);
    }

    [Theory]
    [InlineData(2026, 3, 29, 0, 30, 2)]
    [InlineData(2026, 10, 25, 0, 30, 1)]
    public void Action_based_budget_uses_amsterdam_paid_windows_across_dst(
        int year, int month, int day, int hour, int minute, int expectedHours)
    {
        var transition = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(year, month, day + 1, 9, 0, 0, TimeSpan.Zero);
        var action = CompletedAction(Guid.NewGuid(), transition, end);

        var usage = RealizedParkingBudgetUsageCalculator.CalculateFromActions(
            BudgetPeriod(transition.AddDays(-1), end.AddHours(1), 1500),
            new[] { action }, new[] { Rules(transition.AddDays(-2)) });

        // Sunday is free in Oss. Monday's 09:00 local start follows the DST offset.
        Assert.Equal(TimeSpan.FromHours(expectedHours), usage.UsedPaidDuration);
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

    private static ProviderParkingAction CompletedAction(Guid visitId, DateTimeOffset start, DateTimeOffset end)
    {
        var action = new ProviderParkingAction(Guid.NewGuid(), visitId, start, end);
        action.MarkStarting();
        action.MarkActive($"provider-{Guid.NewGuid():N}", start);
        action.MarkCompleted(end);
        return action;
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

