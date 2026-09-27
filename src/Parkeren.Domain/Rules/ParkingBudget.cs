namespace Parkeren.Domain.Rules;

public sealed class ParkingBudgetPeriod
{
    private ParkingBudgetPeriod() { }

    public ParkingBudgetPeriod(Guid id, DateTimeOffset validFrom, DateTimeOffset validUntil, TimeSpan maximumPaidDuration)
    {
        if (validUntil <= validFrom) throw new ArgumentException("ValidUntil must be after ValidFrom.");
        if (maximumPaidDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumPaidDuration));
        Id = id; ValidFrom = validFrom; ValidUntil = validUntil; MaximumPaidDuration = maximumPaidDuration;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public TimeSpan MaximumPaidDuration { get; private set; }
}

public sealed record ParkingBudgetUsage(
    ParkingBudgetPeriod Period,
    TimeSpan UsedPaidDuration,
    TimeSpan RemainingPaidDuration);

public static class ParkingBudgetCalculator
{
    public static ParkingBudgetUsage Calculate(ParkingBudgetPeriod period, TimeSpan usedPaidDuration)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (usedPaidDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(usedPaidDuration));

        var remaining = period.MaximumPaidDuration - usedPaidDuration;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return new ParkingBudgetUsage(period, usedPaidDuration, remaining);
    }
}
