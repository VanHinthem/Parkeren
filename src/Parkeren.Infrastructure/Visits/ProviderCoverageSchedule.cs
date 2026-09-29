using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Infrastructure.Visits;

internal static class ProviderCoverageSchedule
{
    private static readonly TimeSpan OpenEndedPlanningHorizon = TimeSpan.FromDays(14);

    internal static DateTimeOffset PlanningEndAt(Visit visit, DateTimeOffset fromAt)
    {
        ArgumentNullException.ThrowIfNull(visit);

        if (visit.DesiredEndAt is DateTimeOffset desiredEndAt)
            return desiredEndAt;

        if (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan maxElapsed)
            return visit.StartAt + maxElapsed;

        return fromAt + OpenEndedPlanningHorizon;
    }

    internal static DateTimeOffset PrecheckAt(DateTimeOffset providerEndAt) =>
        providerEndAt.AddMinutes(-5);

    internal static ParkingTimeSegment? NextPaidSegment(
        DateTimeOffset startAt, DateTimeOffset endAt, IReadOnlyCollection<ParkingRuleSet> ruleSets) =>
        ParkingRuleSetPeriodSegmenter.Segment(startAt, endAt, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .FirstOrDefault(x => x.IsPaid);
}
