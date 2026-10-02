using Parkeren.Domain.Rules;

namespace Parkeren.Domain.Visits;

public sealed record VisitTerminalBoundary(DateTimeOffset At, VisitEndReason Reason);

public static class VisitTerminalBoundaryCalculator
{
    private static readonly TimeSpan PaidSearchChunk = TimeSpan.FromDays(28);

    public static VisitTerminalBoundary? Calculate(
        Visit visit,
        IReadOnlyCollection<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(ruleSets);

        var candidates = new List<VisitTerminalBoundary>();

        if (visit.DesiredEndAt is DateTimeOffset desiredEndAt)
            candidates.Add(new VisitTerminalBoundary(desiredEndAt, VisitEndReason.DesiredEndReached));

        if (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan maxElapsed)
            candidates.Add(new VisitTerminalBoundary(
                visit.StartAt + maxElapsed,
                VisitEndReason.MaxVisitElapsedDurationReached));

        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaid)
        {
            if (maxPaid <= TimeSpan.Zero)
                throw new InvalidOperationException("Max paid parking duration must be greater than zero.");

            var searchEndAt = candidates.Count == 0
                ? (DateTimeOffset?)null
                : candidates.MinBy(x => x.At)!.At;

            var paidBoundaryAt = FindPaidBoundary(
                visit.StartAt,
                maxPaid,
                searchEndAt,
                ruleSets);

            if (paidBoundaryAt is DateTimeOffset at)
                candidates.Add(new VisitTerminalBoundary(at, VisitEndReason.MaxPaidParkingDurationReached));
        }

        return candidates
            .OrderBy(x => x.At)
            .ThenBy(x => ReasonPriority(x.Reason))
            .FirstOrDefault();
    }

    private static DateTimeOffset? FindPaidBoundary(
        DateTimeOffset startAt,
        TimeSpan maxPaid,
        DateTimeOffset? searchEndAt,
        IReadOnlyCollection<ParkingRuleSet> ruleSets)
    {
        var remaining = maxPaid;
        var cursor = startAt;

        while (!searchEndAt.HasValue || cursor < searchEndAt.Value)
        {
            var ruleSet = ParkingRuleSetResolver.Resolve(ruleSets, cursor);
            var chunkEnd = AddSafely(cursor, PaidSearchChunk);

            if (ruleSet.ValidUntil is DateTimeOffset validUntil && validUntil < chunkEnd)
                chunkEnd = validUntil;
            if (searchEndAt is DateTimeOffset searchEnd && searchEnd < chunkEnd)
                chunkEnd = searchEnd;

            if (chunkEnd <= cursor)
                throw new InvalidOperationException("Parking rule set did not provide a forward planning interval.");

            foreach (var segment in ParkingTimeSegmenter.Segment(cursor, chunkEnd, ruleSet).Where(x => x.IsPaid))
            {
                var paidDuration = segment.End - segment.Start;
                if (remaining <= paidDuration)
                    return segment.Start + remaining;

                remaining -= paidDuration;
            }

            cursor = chunkEnd;

            if (!searchEndAt.HasValue &&
                ruleSet.ValidUntil is null &&
                !CanProducePaidTimeFrom(ruleSet, cursor))
                return null;
        }

        return null;
    }

    private static bool CanProducePaidTimeFrom(ParkingRuleSet ruleSet, DateTimeOffset fromAt)
    {
        if (ruleSet.PaidWindows.Count > 0)
            return true;

        var zone = TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId);
        var fromDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromAt, zone).Date);
        return ruleSet.CalendarExceptions.Any(x => x.IsPaid && x.Date >= fromDate);
    }

    private static DateTimeOffset AddSafely(DateTimeOffset value, TimeSpan duration)
    {
        var remainingTicks = DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks;
        return duration.Ticks >= remainingTicks ? DateTimeOffset.MaxValue : value + duration;
    }

    private static int ReasonPriority(VisitEndReason reason) => reason switch
    {
        VisitEndReason.MaxPaidParkingDurationReached => 0,
        VisitEndReason.MaxVisitElapsedDurationReached => 1,
        VisitEndReason.DesiredEndReached => 2,
        VisitEndReason.ManualStop => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unsupported Visit end reason.")
    };
}
