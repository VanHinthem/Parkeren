namespace Parkeren.Domain.Visits;

public enum ProviderActionState { Planned, Starting, Active, Stopping, Stopped, Completed, Failed }
public enum ProviderActionHealth { Healthy, Unknown, Reconciling }

public sealed class ProviderParkingAction
{
    private ProviderParkingAction() { }
    public ProviderParkingAction(Guid id, Guid? visitId, DateTimeOffset plannedStartAt, DateTimeOffset plannedEndAt)
    {
        if (plannedEndAt <= plannedStartAt) throw new ArgumentOutOfRangeException(nameof(plannedEndAt));
        Id = id; VisitId = visitId; PlannedStartAt = plannedStartAt; PlannedEndAt = plannedEndAt;
        State = ProviderActionState.Planned; Health = ProviderActionHealth.Healthy; CreatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid? VisitId { get; private set; }
    public string? ProviderActionId { get; private set; }
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
    public void MarkActive(string providerActionId, DateTimeOffset actualStartAt, string? providerStatus = null) { Ensure(ProviderActionState.Starting); ProviderActionId = providerActionId; ActualStartAt = actualStartAt; ProviderStatus = providerStatus; State = ProviderActionState.Active; Health = ProviderActionHealth.Healthy; }
    public void MarkUnknown() { if (State is not ProviderActionState.Starting and not ProviderActionState.Stopping) throw new InvalidOperationException("Only an in-flight provider mutation can become unknown."); Health = ProviderActionHealth.Unknown; }
    public void BeginReconciliation() { if (Health != ProviderActionHealth.Unknown) throw new InvalidOperationException("Only an unknown provider action can be reconciled."); Health = ProviderActionHealth.Reconciling; }
    private void Ensure(ProviderActionState expected) { if (State != expected) throw new InvalidOperationException($"Expected provider action state {expected}, but was {State}."); }
}
