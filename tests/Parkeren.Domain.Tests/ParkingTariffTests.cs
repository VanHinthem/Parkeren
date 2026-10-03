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
    public void Provider_action_cost_rounds_up_once_after_summing_tariff_segments()
    {
        var boundary = new DateTimeOffset(2027, 1, 1, 0, 1, 0, TimeSpan.Zero);
        var oldTariff = new ParkingTariff(Guid.NewGuid(), DateTimeOffset.MinValue, boundary, 0.25m);
        var newTariff = new ParkingTariff(Guid.NewGuid(), boundary, null, 0.25m);
        var actionSegments = new[]
        {
            new ParkingTimeSegment(boundary.AddMinutes(-1), boundary, true),
            new ParkingTimeSegment(boundary, boundary.AddMinutes(1), true)
        };

        var amount = ProviderActionCostCalculator.Calculate(actionSegments, new[] { oldTariff, newTariff });

        Assert.Equal(0.01m, amount);
    }

    [Fact]
    public void Provider_action_cost_rounds_each_action_once_before_summing_actions()
    {
        var start = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tariff = new ParkingTariff(Guid.NewGuid(), DateTimeOffset.MinValue, null, 0.25m);
        var actionSegments = new[]
        {
            new[] { new ParkingTimeSegment(start, start.AddMinutes(1), true) },
            new[] { new ParkingTimeSegment(start.AddMinutes(2), start.AddMinutes(3), true) }
        };

        var amount = actionSegments.Sum(segments =>
            ProviderActionCostCalculator.Calculate(segments, new[] { tariff }));
        var roundedAsOneAction = ProviderActionCostCalculator.Calculate(
            actionSegments.SelectMany(x => x), new[] { tariff });

        Assert.Equal(0.02m, amount);
        Assert.Equal(0.01m, roundedAsOneAction);
    }

    [Fact]
    public void Provider_action_cost_rounds_any_positive_fraction_up_to_a_cent()
    {
        var start = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tariff = new ParkingTariff(Guid.NewGuid(), DateTimeOffset.MinValue, null, 0.30m);
        var actionSegments = new[]
        {
            new ParkingTimeSegment(start, start.AddSeconds(1), true)
        };

        var amount = ProviderActionCostCalculator.Calculate(actionSegments, new[] { tariff });

        Assert.Equal(0.01m, amount);
    }

    [Fact]
    public void Provider_action_cost_keeps_an_exact_cent_at_one_cent()
    {
        var start = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tariff = new ParkingTariff(Guid.NewGuid(), DateTimeOffset.MinValue, null, 36m);
        var actionSegments = new[]
        {
            new ParkingTimeSegment(start, start.AddSeconds(1), true)
        };

        var amount = ProviderActionCostCalculator.Calculate(actionSegments, new[] { tariff });

        Assert.Equal(0.01m, amount);
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
    [Fact]
    public void Overlapping_tariffs_are_rejected()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tariffs = new[]
        {
            new ParkingTariff(Guid.NewGuid(), start, start.AddMonths(6), 1m),
            new ParkingTariff(Guid.NewGuid(), start.AddMonths(5), start.AddYears(1), 2m)
        };

        Assert.Throws<InvalidOperationException>(() => ParkingTariffResolver.ValidateNoOverlap(tariffs));
    }

    [Fact]
    public void Open_tariff_can_be_closed_once_at_later_boundary()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tariff = new ParkingTariff(Guid.NewGuid(), start, null, 1m);
        var boundary = start.AddMonths(6);

        tariff.CloseAt(boundary);

        Assert.Equal(boundary, tariff.ValidUntil);
        Assert.Throws<InvalidOperationException>(() => tariff.CloseAt(boundary.AddMonths(1)));
    }

}
