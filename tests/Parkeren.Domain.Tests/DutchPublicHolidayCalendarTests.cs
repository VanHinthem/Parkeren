using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class DutchPublicHolidayCalendarTests
{
    [Fact]
    public void Calendar_contains_official_2026_holidays()
    {
        var dates = DutchPublicHolidayCalendar.ForYear(2026);
        Assert.Contains(new DateOnly(2026, 1, 1), dates);
        Assert.Contains(new DateOnly(2026, 4, 3), dates);
        Assert.Contains(new DateOnly(2026, 4, 5), dates);
        Assert.Contains(new DateOnly(2026, 4, 6), dates);
        Assert.Contains(new DateOnly(2026, 4, 27), dates);
        Assert.Contains(new DateOnly(2026, 5, 5), dates);
        Assert.Contains(new DateOnly(2026, 5, 14), dates);
        Assert.Contains(new DateOnly(2026, 5, 24), dates);
        Assert.Contains(new DateOnly(2026, 5, 25), dates);
        Assert.Contains(new DateOnly(2026, 12, 25), dates);
        Assert.Contains(new DateOnly(2026, 12, 26), dates);
    }

    [Fact]
    public void Kings_day_moves_to_saturday_when_april_27_is_sunday()
    {
        Assert.Contains(new DateOnly(2025, 4, 26), DutchPublicHolidayCalendar.ForYear(2025));
    }
}
