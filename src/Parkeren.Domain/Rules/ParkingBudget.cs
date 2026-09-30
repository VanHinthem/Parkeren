using Parkeren.Domain.Visits;

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




public static class RealizedParkingBudgetUsageCalculator
{
    public static ParkingBudgetUsage Calculate(
        ParkingBudgetPeriod period,
        IEnumerable<Visit> visits,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(ruleSets);

        var ruleSetArray = ruleSets.ToArray();
        var usedPaidDuration = TimeSpan.Zero;

        foreach (var visit in visits.Where(x =>
                     x.Status == VisitStatus.Completed &&
                     x.ActualEndAt.HasValue &&
                     x.StartAt < period.ValidUntil &&
                     x.ActualEndAt.Value > period.ValidFrom))
        {
            var start = visit.StartAt > period.ValidFrom ? visit.StartAt : period.ValidFrom;
            var end = visit.ActualEndAt!.Value < period.ValidUntil ? visit.ActualEndAt.Value : period.ValidUntil;
            if (end <= start)
                continue;

            var paidDuration = ParkingRuleSetPeriodSegmenter.Segment(start, end, ruleSetArray)
                .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
                .Where(x => x.IsPaid)
                .Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));

            usedPaidDuration += paidDuration;
        }

        return ParkingBudgetCalculator.Calculate(period, usedPaidDuration);
    }
}

public sealed class ParkingBudgetWarningState
{
    private ParkingBudgetWarningState() { }

    public ParkingBudgetWarningState(
        Guid id,
        Guid parkingBudgetPeriodId,
        int thresholdPercentage,
        DateTimeOffset notifiedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Budget warning state id is required.", nameof(id));
        if (parkingBudgetPeriodId == Guid.Empty) throw new ArgumentException("Parking budget period id is required.", nameof(parkingBudgetPeriodId));
        if (thresholdPercentage <= 0 || thresholdPercentage > 100) throw new ArgumentOutOfRangeException(nameof(thresholdPercentage));

        Id = id;
        ParkingBudgetPeriodId = parkingBudgetPeriodId;
        ThresholdPercentage = thresholdPercentage;
        NotifiedAt = notifiedAt;
    }

    public Guid Id { get; private set; }
    public Guid ParkingBudgetPeriodId { get; private set; }
    public int ThresholdPercentage { get; private set; }
    public DateTimeOffset NotifiedAt { get; private set; }
}

public static class ParkingBudgetWarningEvaluator
{
    public static IReadOnlyList<int> GetNewlyReachedThresholds(
        ParkingBudgetUsage usage,
        IEnumerable<int> thresholds,
        IEnumerable<int> alreadyNotifiedThresholds)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(alreadyNotifiedThresholds);

        if (usage.Period.MaximumPaidDuration <= TimeSpan.Zero)
            return Array.Empty<int>();

        var notified = alreadyNotifiedThresholds.ToHashSet();
        var usedPercentage = usage.UsedPaidDuration.TotalMilliseconds /
            usage.Period.MaximumPaidDuration.TotalMilliseconds * 100d;

        return thresholds
            .Distinct()
            .OrderBy(x => x)
            .Where(x => x > 0 && x <= 100 && x <= usedPercentage && !notified.Contains(x))
            .ToArray();
    }
}
