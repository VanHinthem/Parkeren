namespace Parkeren.Domain.Rules;

public sealed record ParkingUsage(TimeSpan PaidDuration)
{
    public decimal PaidHours => (decimal)PaidDuration.TotalMinutes / 60m;
}

public static class ParkingUsageCalculator
{
    public static ParkingUsage Calculate(IEnumerable<ParkingTimeSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var paidDuration = segments
            .Where(x => x.IsPaid)
            .Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));

        return new ParkingUsage(paidDuration);
    }
}

public sealed record ParkingCost(decimal Amount);

public static class ParkingCostCalculator
{
    public static ParkingCost Calculate(ParkingUsage usage, decimal hourlyRate)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (hourlyRate < 0m) throw new ArgumentOutOfRangeException(nameof(hourlyRate));

        return new ParkingCost(decimal.Round(usage.PaidHours * hourlyRate, 2, MidpointRounding.AwayFromZero));
    }
}
