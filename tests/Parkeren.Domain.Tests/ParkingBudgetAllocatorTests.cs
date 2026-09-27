using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingBudgetAllocatorTests
{
    [Fact]
    public void Paid_segment_crossing_budget_boundary_is_allocated_to_both_periods()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldPeriod = new ParkingBudgetPeriod(Guid.NewGuid(), boundary.AddYears(-1), boundary, TimeSpan.FromHours(1500));
        var newPeriod = new ParkingBudgetPeriod(Guid.NewGuid(), boundary, boundary.AddYears(1), TimeSpan.FromHours(1500));
        var segment = new ParkingTimeSegment(boundary.AddHours(-2), boundary.AddHours(3), true);

        var allocations = ParkingBudgetAllocator.Allocate(segment, new[] { oldPeriod, newPeriod });

        Assert.Equal(2, allocations.Count);
        Assert.Same(oldPeriod, allocations[0].Period);
        Assert.Equal(TimeSpan.FromHours(2), allocations[0].PaidDuration);
        Assert.Same(newPeriod, allocations[1].Period);
        Assert.Equal(TimeSpan.FromHours(3), allocations[1].PaidDuration);
    }

    [Fact]
    public void Free_segment_consumes_no_budget()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var period = new ParkingBudgetPeriod(Guid.NewGuid(), start, start.AddYears(1), TimeSpan.FromHours(1500));

        Assert.Empty(ParkingBudgetAllocator.Allocate(
            new ParkingTimeSegment(start.AddHours(1), start.AddHours(5), false),
            new[] { period }));
    }
}
