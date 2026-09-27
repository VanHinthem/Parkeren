namespace Parkeren.Domain.Rules;

public static class ParkingBudgetPeriodResolver
{
    public static ParkingBudgetPeriod Resolve(
        IEnumerable<ParkingBudgetPeriod> periods,
        DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(periods);

        var applicable = periods.Where(x =>
            x.ValidFrom <= instant && instant < x.ValidUntil).ToArray();

        return applicable.Length switch
        {
            1 => applicable[0],
            0 => throw new InvalidOperationException($"No parking budget period applies at {instant:O}."),
            _ => throw new InvalidOperationException($"Multiple parking budget periods apply at {instant:O}.")
        };
    }

    public static void ValidateNoOverlap(IEnumerable<ParkingBudgetPeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var ordered = periods.OrderBy(x => x.ValidFrom).ToArray();

        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].ValidFrom < ordered[i - 1].ValidUntil)
                throw new InvalidOperationException("Parking budget periods may not overlap.");
        }
    }
}

public sealed record ParkingBudgetAllocation(
    ParkingBudgetPeriod Period,
    TimeSpan PaidDuration);

public static class ParkingBudgetAllocator
{
    public static IReadOnlyList<ParkingBudgetAllocation> Allocate(
        ParkingTimeSegment paidSegment,
        IEnumerable<ParkingBudgetPeriod> periods)
    {
        if (!paidSegment.IsPaid) return Array.Empty<ParkingBudgetAllocation>();

        var periodArray = periods.ToArray();
        ParkingBudgetPeriodResolver.ValidateNoOverlap(periodArray);

        var boundaries = periodArray
            .SelectMany(x => new[] { x.ValidFrom, x.ValidUntil })
            .Where(x => x > paidSegment.Start && x < paidSegment.End)
            .Append(paidSegment.Start)
            .Append(paidSegment.End)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var result = new List<ParkingBudgetAllocation>();
        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1];
            result.Add(new ParkingBudgetAllocation(
                ParkingBudgetPeriodResolver.Resolve(periodArray, start),
                end - start));
        }

        return result;
    }
}
