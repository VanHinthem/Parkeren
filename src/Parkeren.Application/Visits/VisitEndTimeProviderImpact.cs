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
            action.State == ProviderActionState.Active &&
            action.PlannedEndAt > requestedEndAt)
            ? VisitEndTimeProviderImpact.RequiresProviderMutation
            : VisitEndTimeProviderImpact.None;
    }
}
