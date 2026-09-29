using Parkeren.Domain.Rules;

namespace Parkeren.Infrastructure.Visits;

internal static class ProviderCoverageSchedule
{
    internal static DateTimeOffset PrecheckAt(DateTimeOffset providerEndAt) =>
        providerEndAt.AddMinutes(-5);

    internal static ParkingTimeSegment? NextPaidSegment(
        DateTimeOffset startAt, DateTimeOffset endAt, IReadOnlyCollection<ParkingRuleSet> ruleSets) =>
        ParkingRuleSetPeriodSegmenter.Segment(startAt, endAt, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .FirstOrDefault(x => x.IsPaid);
}
