namespace Parkeren.Domain.Rules;

public sealed class ParkingTariff
{
    private ParkingTariff() { }

    public ParkingTariff(Guid id, DateTimeOffset validFrom, DateTimeOffset? validUntil, decimal hourlyRate)
    {
        if (validUntil.HasValue && validUntil.Value <= validFrom)
            throw new ArgumentException("ValidUntil must be after ValidFrom.");
        if (hourlyRate < 0m)
            throw new ArgumentOutOfRangeException(nameof(hourlyRate));

        Id = id;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        HourlyRate = hourlyRate;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidUntil { get; private set; }
    public decimal HourlyRate { get; private set; }
}

public static class ParkingTariffResolver
{
    public static void ValidateNoOverlap(IEnumerable<ParkingTariff> tariffs)
    {
        ArgumentNullException.ThrowIfNull(tariffs);
        var ordered = tariffs.OrderBy(x => x.ValidFrom).ToArray();

        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = ordered[i - 1];
            if (!previous.ValidUntil.HasValue || ordered[i].ValidFrom < previous.ValidUntil.Value)
                throw new InvalidOperationException("Parking tariffs may not overlap.");
        }
    }

    public static ParkingTariff Resolve(IEnumerable<ParkingTariff> tariffs, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(tariffs);

        var applicable = tariffs.Where(x =>
            x.ValidFrom <= instant &&
            (!x.ValidUntil.HasValue || instant < x.ValidUntil.Value)).ToArray();

        return applicable.Length switch
        {
            1 => applicable[0],
            0 => throw new InvalidOperationException($"No parking tariff applies at {instant:O}."),
            _ => throw new InvalidOperationException($"Multiple parking tariffs apply at {instant:O}.")
        };
    }
}

public sealed record ParkingTariffCost(ParkingTariff Tariff, TimeSpan PaidDuration, decimal Amount);

public static class ParkingTariffCostCalculator
{
    public static IReadOnlyList<ParkingTariffCost> Calculate(
        ParkingTimeSegment paidSegment,
        IEnumerable<ParkingTariff> tariffs)
    {
        if (!paidSegment.IsPaid) return Array.Empty<ParkingTariffCost>();

        var tariffArray = tariffs.ToArray();
        var boundaries = tariffArray
            .SelectMany(x => new DateTimeOffset?[] { x.ValidFrom, x.ValidUntil })
            .Where(x => x.HasValue && x.Value > paidSegment.Start && x.Value < paidSegment.End)
            .Select(x => x!.Value)
            .Append(paidSegment.Start)
            .Append(paidSegment.End)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var result = new List<ParkingTariffCost>();
        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1];
            var tariff = ParkingTariffResolver.Resolve(tariffArray, start);
            var duration = end - start;
            var amount = ParkingCostCalculator.Calculate(new ParkingUsage(duration), tariff.HourlyRate).Amount;
            result.Add(new ParkingTariffCost(tariff, duration, amount));
        }

        return result;
    }
}
