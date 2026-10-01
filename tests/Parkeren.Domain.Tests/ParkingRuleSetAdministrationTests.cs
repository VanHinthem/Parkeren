using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingRuleSetAdministrationTests
{
    [Fact]
    public void Overlapping_paid_windows_on_same_day_are_rejected()
    {
        var windows = new[]
        {
            new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0)),
            new PaidWindow(DayOfWeek.Monday, new TimeOnly(11, 0), new TimeOnly(13, 0))
        };

        Assert.Throws<ArgumentException>(() => new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            TimeSpan.FromHours(4),
            windows));
    }

    [Fact]
    public void Adjacent_paid_windows_on_same_day_are_allowed()
    {
        var windows = new[]
        {
            new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0)),
            new PaidWindow(DayOfWeek.Monday, new TimeOnly(12, 0), new TimeOnly(20, 0))
        };

        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            TimeSpan.FromHours(4),
            windows);

        Assert.Equal(2, rules.PaidWindows.Count);
    }

    [Fact]
    public void Duplicate_calendar_exception_dates_are_rejected()
    {
        var date = new DateOnly(2027, 1, 1);
        var exceptions = new[]
        {
            new ParkingCalendarException(date, false),
            new ParkingCalendarException(date, true)
        };

        Assert.Throws<ArgumentException>(() => new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>(),
            exceptions));
    }

    [Fact]
    public void Open_rule_set_can_be_closed_once_at_later_boundary()
    {
        var validFrom = DateTimeOffset.UtcNow;
        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            validFrom,
            null,
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>());
        var validUntil = validFrom.AddDays(1);

        rules.CloseAt(validUntil);

        Assert.Equal(validUntil, rules.ValidUntil);
        Assert.Throws<InvalidOperationException>(() => rules.CloseAt(validUntil.AddDays(1)));
    }
}
