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
    public Guid? ProviderProductId { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public TimeSpan MaximumPaidDuration { get; private set; }
    public void AssignProviderProduct(Guid providerProductId)
    {
        if (providerProductId == Guid.Empty)
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (ProviderProductId.HasValue && ProviderProductId.Value != providerProductId)
            throw new InvalidOperationException("Parking budget period is already assigned to another provider product.");

        ProviderProductId = providerProductId;
    }
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
    /// <summary>
    /// Calculates realized paid time from distinct terminated provider actions,
    /// regardless of whether an action belongs to a Visit. Callers must supply
    /// actions for the budget period's provider product.
    /// </summary>
    public static ParkingBudgetUsage CalculateFromActions(
        ParkingBudgetPeriod period,
        IEnumerable<ProviderParkingAction> providerActions,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(providerActions);
        ArgumentNullException.ThrowIfNull(ruleSets);

        var completed = providerActions
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .Where(x => x.State is ProviderActionState.Completed or ProviderActionState.Stopped)
            .ToArray();
        var segments = ProviderActionPaidTimeCalculator.CalculatePaidSegments(
            completed, period.ValidFrom, period.ValidUntil, ruleSets)
            .OrderBy(x => x.Start)
            .ToArray();

        var used = TimeSpan.Zero;
        DateTimeOffset? coveredUntil = null;
        foreach (var segment in segments)
        {
            var from = coveredUntil.HasValue && coveredUntil.Value > segment.Start
                ? coveredUntil.Value : segment.Start;
            if (segment.End > from)
                used += segment.End - from;
            if (!coveredUntil.HasValue || segment.End > coveredUntil.Value)
                coveredUntil = segment.End;
        }

        return ParkingBudgetCalculator.Calculate(period, used);
    }

    public static ParkingBudgetUsage Calculate(
        ParkingBudgetPeriod period,
        IEnumerable<Visit> visits,
        IEnumerable<ProviderParkingAction> providerActions,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(providerActions);
        ArgumentNullException.ThrowIfNull(ruleSets);

        var ruleSetArray = ruleSets.ToArray();
        var visitArray = visits.Where(x => x.Status == VisitStatus.Completed).ToArray();
        var visitIds = visitArray.Select(x => x.Id).ToHashSet();
        var actionsByVisit = providerActions
            .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
            .GroupBy(x => x.VisitId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var usedPaidDuration = TimeSpan.Zero;

        foreach (var visit in visitArray)
        {
            if (!actionsByVisit.TryGetValue(visit.Id, out var visitActions))
                continue;

            usedPaidDuration += ProviderActionPaidTimeCalculator.Calculate(
                visitActions,
                period.ValidFrom,
                period.ValidUntil,
                ruleSetArray);
        }

        return ParkingBudgetCalculator.Calculate(period, usedPaidDuration);
    }
}

public static class ProviderActionPaidTimeCalculator
{
    public static TimeSpan Calculate(
        IEnumerable<ProviderParkingAction> providerActions,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        return CalculatePaidSegments(providerActions, rangeStart, rangeEnd, ruleSets)
            .Aggregate(TimeSpan.Zero, (duration, segment) => duration + (segment.End - segment.Start));
    }

    public static IReadOnlyList<ParkingTimeSegment> CalculatePaidSegments(
        IEnumerable<ProviderParkingAction> providerActions,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(providerActions);
        ArgumentNullException.ThrowIfNull(ruleSets);
        if (rangeEnd < rangeStart) throw new ArgumentOutOfRangeException(nameof(rangeEnd));

        var ruleSetArray = ruleSets.ToArray();
        var paidSegments = new List<ParkingTimeSegment>();
        foreach (var action in providerActions)
        {
            if (action.State == ProviderActionState.Failed)
                continue;
            if (action.State is not (ProviderActionState.Stopped or ProviderActionState.Completed))
                throw new InvalidOperationException("A completed Visit contains a provider action that has not terminated.");
            if (action.ActualEndAt is not DateTimeOffset actualEndAt)
                throw new InvalidOperationException("A terminated provider action has no actual end time.");
            if (action.ActualStartAt is not DateTimeOffset actualStartAt)
            {
                if (action.State == ProviderActionState.Stopped && actualEndAt <= action.PlannedStartAt)
                    continue;

                throw new InvalidOperationException("A terminated provider action has no actual start time.");
            }
            if (actualEndAt < actualStartAt)
                throw new InvalidOperationException("A provider action has an invalid actual interval.");

            var start = actualStartAt > rangeStart ? actualStartAt : rangeStart;
            var end = actualEndAt < rangeEnd ? actualEndAt : rangeEnd;
            if (end <= start)
                continue;

            paidSegments.AddRange(ParkingRuleSetPeriodSegmenter.Segment(start, end, ruleSetArray)
                .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
                .Where(x => x.IsPaid));
        }

        return paidSegments;
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
