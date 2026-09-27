using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ParkingTariffTests
{
    [Fact]
    public void Paid_segment_crossing_tariff_boundary_uses_both_tariffs()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var oldTariff = new ParkingTariff(Guid.NewGuid(), DateTimeOffset.MinValue, boundary, 2m);
        var newTariff = new ParkingTariff(Guid.NewGuid(), boundary, null, 3m);
        var segment = new ParkingTimeSegment(boundary.AddHours(-1), boundary.AddHours(2), true);

        var costs = ParkingTariffCostCalculator.Calculate(segment, new[] { oldTariff, newTariff });

        Assert.Equal(2, costs.Count);
        Assert.Equal(2m, costs[0].Amount);
        Assert.Equal(6m, costs[1].Amount);
        Assert.Equal(8m, costs.Sum(x => x.Amount));
    }

    [Fact]
    public void Free_segment_has_no_tariff_cost()
    {
        var tariff = new ParkingTariff(Guid.NewGuid(), DateTimeOffset.MinValue, null, 2m);
        var start = new DateTimeOffset(2026, 9, 28, 20, 0, 0, TimeSpan.Zero);

        Assert.Empty(ParkingTariffCostCalculator.Calculate(
            new ParkingTimeSegment(start, start.AddHours(10), false),
            new[] { tariff }));
    }
}
