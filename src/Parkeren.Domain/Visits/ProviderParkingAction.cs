namespace Parkeren.Domain.Visits;

public enum ProviderActionState { Planned, Starting, Scheduled, Active, Stopping, Stopped, Completed, Failed }
public enum ProviderActionHealth { Healthy, Unknown, Reconciling }

public sealed class ProviderParkingAction
{
    private ProviderParkingAction() { }
    public ProviderParkingAction(
        Guid id,
        Guid? visitId,
        DateTimeOffset plannedStartAt,
        DateTimeOffset plannedEndAt,
        string? providerProductId = null,
        string? providerLocation = null)
    {
        if (plannedEndAt <= plannedStartAt) throw new ArgumentOutOfRangeException(nameof(plannedEndAt));
        Id = id; VisitId = visitId; PlannedStartAt = plannedStartAt; PlannedEndAt = plannedEndAt;
        ProviderProductId = providerProductId;
        ProviderLocation = providerLocation;
        State = ProviderActionState.Planned; Health = ProviderActionHealth.Healthy; CreatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid? VisitId { get; private set; }
    public string? ProviderActionId { get; private set; }
    public string? ProviderProductId { get; private set; }
    public string? ProviderLocation { get; private set; }
    public DateTimeOffset PlannedStartAt { get; private set; }
    public DateTimeOffset PlannedEndAt { get; private set; }
    public DateTimeOffset? ActualStartAt { get; private set; }
    public DateTimeOffset? ActualEndAt { get; private set; }
    public string? ProviderStatus { get; private set; }
    public ProviderActionState State { get; private set; }
    public ProviderActionHealth Health { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; private set; }
    public void MarkStarting() { Ensure(ProviderActionState.Planned); State = ProviderActionState.Starting; }
    public void CaptureStartResponse(string providerActionId, DateTimeOffset actualStartAt, string? providerStatus = null) { Ensure(ProviderActionState.Starting); if (string.IsNullOrWhiteSpace(providerActionId)) throw new ArgumentException("Provider action id is required.", nameof(providerActionId)); ProviderActionId = providerActionId; ActualStartAt = actualStartAt; ProviderStatus = providerStatus; }
    public void MarkActive(string providerActionId, DateTimeOffset actualStartAt, string? providerStatus = null) { Ensure(ProviderActionState.Starting); CaptureStartResponse(providerActionId, actualStartAt, providerStatus); State = ProviderActionState.Active; Health = ProviderActionHealth.Healthy; }
    public void MarkScheduled(string providerActionId, string? providerStatus = null)
    {
        Ensure(ProviderActionState.Starting);
        if (string.IsNullOrWhiteSpace(providerActionId)) throw new ArgumentException("Provider action id is required.", nameof(providerActionId));
        ProviderActionId = providerActionId;
        ProviderStatus = providerStatus;
        ActualStartAt = null;
        State = ProviderActionState.Scheduled;
        Health = ProviderActionHealth.Healthy;
    }
    public void ActivateScheduled(DateTimeOffset actualStartAt, string? providerStatus = null)
    {
        Ensure(ProviderActionState.Scheduled);
        ActualStartAt = actualStartAt;
        ProviderStatus = providerStatus;
        State = ProviderActionState.Active;
        Health = ProviderActionHealth.Healthy;
    }
    public void ExtendPlannedEnd(DateTimeOffset newEndAt)
    {
        Ensure(ProviderActionState.Active);
        if (newEndAt <= PlannedEndAt)
            throw new ArgumentOutOfRangeException(nameof(newEndAt), "Extended provider action end must be later than the current planned end.");

        PlannedEndAt = newEndAt;
    }

    public void BeginStopping()
    {
        if (State is not (ProviderActionState.Active or ProviderActionState.Scheduled))
            throw new InvalidOperationException($"Only an active or scheduled provider action can be stopped, but was {State}.");
        if (string.IsNullOrWhiteSpace(ProviderActionId))
            throw new InvalidOperationException("A provider action must have a provider action id before it can be stopped.");
        State = ProviderActionState.Stopping;
    }

    public void MarkCompleted(DateTimeOffset endedAt)
    {
        Ensure(ProviderActionState.Active);
        if (ActualStartAt is DateTimeOffset startedAt && endedAt < startedAt)
            throw new ArgumentOutOfRangeException(nameof(endedAt));
        ActualEndAt = endedAt;
        State = ProviderActionState.Completed;
        Health = ProviderActionHealth.Healthy;
    }

    public void MarkExternallyStopped(string providerStatus)
    {
        Ensure(ProviderActionState.Active);
        if (string.IsNullOrWhiteSpace(providerStatus))
            throw new ArgumentException("Provider status is required.", nameof(providerStatus));
        ProviderStatus = providerStatus;
        State = ProviderActionState.Stopped;
        Health = ProviderActionHealth.Healthy;
        // The provider did not supply an actual stop instant; observation time is not that instant.
    }

    public void MarkProviderMissing()
    {
        Ensure(ProviderActionState.Stopping);
        ProviderStatus = "missing";
        State = ProviderActionState.Stopped;
        Health = ProviderActionHealth.Healthy;
        // Provider absence proves there is no action left to stop, but not when it ended.
    }

    public void MarkStopped(DateTimeOffset actualEndAt, string? providerStatus = null)
    {
        Ensure(ProviderActionState.Stopping);
        if (ActualStartAt is not null && actualEndAt < ActualStartAt)
            throw new ArgumentOutOfRangeException(nameof(actualEndAt));
        ActualEndAt = actualEndAt;
        ProviderStatus = providerStatus;
        State = ProviderActionState.Stopped;
        Health = ProviderActionHealth.Healthy;
    }

    public void MarkFailed() { if (State != ProviderActionState.Starting) throw new InvalidOperationException("Only a starting provider action can fail."); State = ProviderActionState.Failed; Health = ProviderActionHealth.Healthy; }
    public void MarkUnknown() { if (State is not ProviderActionState.Starting and not ProviderActionState.Stopping) throw new InvalidOperationException("Only an in-flight provider mutation can become unknown."); Health = ProviderActionHealth.Unknown; }
    public void BeginReconciliation() { if (Health != ProviderActionHealth.Unknown) throw new InvalidOperationException("Only an unknown provider action can be reconciled."); Health = ProviderActionHealth.Reconciling; }
    public void ResetForRetry() { if (State != ProviderActionState.Starting || Health != ProviderActionHealth.Unknown) throw new InvalidOperationException("Only an unknown starting provider action can be retried after reconciliation established no provider action exists."); if (!string.IsNullOrWhiteSpace(ProviderActionId)) throw new InvalidOperationException("A provider action with a captured provider id cannot be reset for retry."); State = ProviderActionState.Planned; Health = ProviderActionHealth.Healthy; ActualStartAt = null; ProviderStatus = null; }
    private void Ensure(ProviderActionState expected) { if (State != expected) throw new InvalidOperationException($"Expected provider action state {expected}, but was {State}."); }
}
