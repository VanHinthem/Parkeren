using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record VisitRecoveryItem(
    Visit Visit,
    IReadOnlyList<ProviderParkingAction> ProviderActions,
    IReadOnlyList<ProviderOperation> UnresolvedOperations);

public interface IVisitRecoveryService
{
    Task<IReadOnlyList<VisitRecoveryItem>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task RecoverAsync(
        CancellationToken cancellationToken = default);

    Task RecoverExpiredInProgressOperationsAsync(
        CancellationToken cancellationToken = default);

    Task ReconcileActiveProviderActionsAsync(
        CancellationToken cancellationToken = default);

    Task ReconcileUnknownOperationsAsync(
        CancellationToken cancellationToken = default);
}

public enum VisitRecoveryKind
{
    ReconcileStart,
    ReconcileContinuationStart,
    ReconcileExtend,
    ReconcileStop,
    ReconcileScheduledCancel,
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

            (VisitStatus.Active, ProviderOperationType.ContinueStart) =>
                new(item, VisitRecoveryKind.ReconcileContinuationStart, operation),

            (VisitStatus.Stopping, ProviderOperationType.Stop) =>
                new(item, VisitRecoveryKind.ReconcileStop, operation),

            (VisitStatus.Active, ProviderOperationType.Stop) =>
                new(item, VisitRecoveryKind.ReconcileScheduledCancel, operation),

            _ => new(item, VisitRecoveryKind.Ambiguous, operation)
        };
    }
}
