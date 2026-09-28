using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record VisitRecoveryItem(
    Visit Visit,
    IReadOnlyList<ProviderParkingAction> ProviderActions,
    IReadOnlyList<ProviderOperation> UnresolvedOperations);

public enum VisitRecoveryKind
{
    ReconcileStart,
    ReconcileExtend,
    ReconcileStop,
    RebuildScheduler,
    Ambiguous
}

public sealed record VisitRecoveryDecision(
    VisitRecoveryItem Item,
    VisitRecoveryKind Kind,
    ProviderOperation? Operation = null);

public static class VisitRecoveryClassifier
{
    public static VisitRecoveryDecision Classify(VisitRecoveryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var unresolved = item.UnresolvedOperations.ToArray();

        if (unresolved.Length == 0)
        {
            return item.Visit.Status == VisitStatus.Active
                ? new(item, VisitRecoveryKind.RebuildScheduler)
                : new(item, VisitRecoveryKind.Ambiguous);
        }

        if (unresolved.Length != 1)
            return new(item, VisitRecoveryKind.Ambiguous);

        var operation = unresolved[0];

        return (item.Visit.Status, operation.Type) switch
        {
            (VisitStatus.Starting, ProviderOperationType.Start) =>
                new(item, VisitRecoveryKind.ReconcileStart, operation),

            (VisitStatus.Active, ProviderOperationType.Extend) =>
                new(item, VisitRecoveryKind.ReconcileExtend, operation),

            (VisitStatus.Stopping, ProviderOperationType.Stop) =>
                new(item, VisitRecoveryKind.ReconcileStop, operation),

            _ => new(item, VisitRecoveryKind.Ambiguous, operation)
        };
    }
}
