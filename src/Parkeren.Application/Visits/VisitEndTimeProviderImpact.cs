using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public enum VisitEndTimeProviderImpact
{
    None,
    RequiresProviderMutation
}

public static class VisitEndTimeProviderImpactClassifier
{
    public static VisitEndTimeProviderImpact Classify(
        DateTimeOffset requestedEndAt,
        IEnumerable<ProviderParkingAction> providerActions)
    {
        ArgumentNullException.ThrowIfNull(providerActions);

        return providerActions.Any(action =>
            action.State is ProviderActionState.Active or ProviderActionState.Scheduled &&
            action.PlannedEndAt > requestedEndAt)
            ? VisitEndTimeProviderImpact.RequiresProviderMutation
            : VisitEndTimeProviderImpact.None;
    }
}
