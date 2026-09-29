using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public enum VisitEndTimeProviderImpact
{
    None,
    CancelScheduled,
    ReplaceScheduled,
    ShortenActive
}

public static class VisitEndTimeProviderImpactClassifier
{
    public static VisitEndTimeProviderImpact Classify(
        DateTimeOffset requestedEndAt,
        IEnumerable<ProviderParkingAction> providerActions)
    {
        ArgumentNullException.ThrowIfNull(providerActions);

        var affected = providerActions
            .Where(x => x.PlannedEndAt > requestedEndAt)
            .ToList();

        if (affected.Any(x => x.State == ProviderActionState.Active))
            return VisitEndTimeProviderImpact.ShortenActive;

        var scheduled = affected
            .Where(x => x.State == ProviderActionState.Scheduled)
            .OrderBy(x => x.PlannedStartAt)
            .FirstOrDefault();

        if (scheduled is null)
            return VisitEndTimeProviderImpact.None;

        return requestedEndAt <= scheduled.PlannedStartAt
            ? VisitEndTimeProviderImpact.CancelScheduled
            : VisitEndTimeProviderImpact.ReplaceScheduled;
    }
}
