namespace Parkeren.Domain.Visits;

public static class ProviderOperationStartupRecovery
{
    public static readonly TimeSpan AttemptLease = TimeSpan.FromMinutes(5);

    public static bool ResumeInterruptedReconciliation(
        ProviderOperation operation,
        ProviderParkingAction action)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);

        if (operation.Status != ProviderOperationStatus.Reconciling)
            return false;

        if (operation.ProviderParkingActionId != action.Id)
            throw new InvalidOperationException("Provider operation does not reference the supplied provider action.");

        operation.ResumeUnknownAfterInterruptedReconciliation();
        if (action.Health == ProviderActionHealth.Reconciling)
            action.ResumeUnknownAfterInterruptedReconciliation();

        return true;
    }

    public static bool MarkStaleInProgressUnknown(
        ProviderOperation operation,
        ProviderParkingAction action,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);

        if (operation.Status != ProviderOperationStatus.InProgress)
            return false;

        if (operation.ProviderParkingActionId != action.Id)
            throw new InvalidOperationException("Provider operation does not reference the supplied provider action.");

        if (operation.AttemptStartedAt is DateTimeOffset startedAt &&
            now - startedAt < AttemptLease)
            return false;

        operation.MarkUnknown("stale-in-progress");

        if (action.State is ProviderActionState.Starting or ProviderActionState.Stopping)
            action.MarkUnknown();

        return true;
    }
}
