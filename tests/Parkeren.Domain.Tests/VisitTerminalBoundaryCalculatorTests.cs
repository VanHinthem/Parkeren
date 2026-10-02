using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Domain.Tests;

public sealed class VisitTerminalBoundaryCalculatorTests
{
    [Fact]
    public void No_limits_returns_no_terminal_boundary()
    {
        var visit = CreateVisit(
            startAt: new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            desiredEndAt: null,
            maxPaid: null,
            maxElapsed: null);

        Assert.Null(VisitTerminalBoundaryCalculator.Calculate(visit, Array.Empty<ParkingRuleSet>()));
    }

    [Fact]
    public void Desired_end_is_used_when_it_is_the_only_boundary()
    {
        var startAt = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var desiredEndAt = startAt.AddHours(3);
        var visit = CreateVisit(startAt, desiredEndAt, null, null);

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, Array.Empty<ParkingRuleSet>());

        Assert.NotNull(boundary);
        Assert.Equal(desiredEndAt, boundary.At);
        Assert.Equal(VisitEndReason.DesiredEndReached, boundary.Reason);
    }

    [Fact]
    public void Elapsed_limit_wins_when_before_desired_end()
    {
        var startAt = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var visit = CreateVisit(startAt, startAt.AddHours(8), null, TimeSpan.FromHours(6));

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, Array.Empty<ParkingRuleSet>());

        Assert.NotNull(boundary);
        Assert.Equal(startAt.AddHours(6), boundary.At);
        Assert.Equal(VisitEndReason.MaxVisitElapsedDurationReached, boundary.Reason);
    }

    [Fact]
    public void Paid_limit_skips_free_overnight_time()
    {
        var startAt = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.FromHours(2));
        var visit = CreateVisit(startAt, null, TimeSpan.FromHours(4), null);
        var rules = CreateWeekdayRules(startAt.AddDays(-10));

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, [rules]);

        Assert.NotNull(boundary);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.FromHours(2)), boundary.At);
        Assert.Equal(VisitEndReason.MaxPaidParkingDurationReached, boundary.Reason);
    }

    [Fact]
    public void Desired_end_wins_when_paid_limit_is_not_consumed_before_it()
    {
        var startAt = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.FromHours(2));
        var desiredEndAt = startAt.AddHours(2);
        var visit = CreateVisit(startAt, desiredEndAt, TimeSpan.FromHours(4), null);
        var rules = CreateWeekdayRules(startAt.AddDays(-10));

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, [rules]);

        Assert.NotNull(boundary);
        Assert.Equal(desiredEndAt, boundary.At);
        Assert.Equal(VisitEndReason.DesiredEndReached, boundary.Reason);
    }

    [Fact]
    public void Equal_boundaries_use_paid_then_elapsed_then_desired_priority()
    {
        var startAt = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var boundaryAt = startAt.AddHours(2);
        var visit = CreateVisit(startAt, boundaryAt, TimeSpan.FromHours(2), TimeSpan.FromHours(2));
        var rules = CreateWeekdayRules(startAt.AddDays(-10));

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, [rules]);

        Assert.NotNull(boundary);
        Assert.Equal(boundaryAt, boundary.At);
        Assert.Equal(VisitEndReason.MaxPaidParkingDurationReached, boundary.Reason);
    }

    [Fact]
    public void Equal_elapsed_and_desired_boundaries_use_elapsed_priority()
    {
        var startAt = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var boundaryAt = startAt.AddHours(2);
        var visit = CreateVisit(startAt, boundaryAt, null, TimeSpan.FromHours(2));

        var boundary = VisitTerminalBoundaryCalculator.Calculate(visit, Array.Empty<ParkingRuleSet>());

        Assert.NotNull(boundary);
        Assert.Equal(boundaryAt, boundary.At);
        Assert.Equal(VisitEndReason.MaxVisitElapsedDurationReached, boundary.Reason);
    }

    [Fact]
    public void Open_ended_visit_with_no_future_paid_time_has_no_paid_boundary()
    {
        var startAt = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var visit = CreateVisit(startAt, null, TimeSpan.FromHours(1), null);
        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            startAt.AddDays(-1),
            null,
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>());

        Assert.Null(VisitTerminalBoundaryCalculator.Calculate(visit, [rules]));
    }

    private static Visit CreateVisit(
        DateTimeOffset startAt,
        DateTimeOffset? desiredEndAt,
        TimeSpan? maxPaid,
        TimeSpan? maxElapsed) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            startAt,
            desiredEndAt,
            EffectiveParkingPolicySnapshot.Capture(
                new EffectiveParkingPolicy(maxPaid, maxElapsed, allowVisitExtension: true)));

    private static ParkingRuleSet CreateWeekdayRules(DateTimeOffset validFrom) =>
        new(
            Guid.NewGuid(),
            validFrom,
            null,
            TimeSpan.FromHours(4),
            Enumerable.Range((int)DayOfWeek.Monday, 6)
                .Select(day => new PaidWindow(
                    (DayOfWeek)day,
                    new TimeOnly(9, 0),
                    new TimeOnly(20, 0)))
                .ToArray(),
            publicHolidaysAreFree: true);
}
