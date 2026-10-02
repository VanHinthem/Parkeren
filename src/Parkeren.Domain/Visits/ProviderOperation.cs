namespace Parkeren.Domain.Visits;

public enum ProviderOperationType { Start, ContinueStart, Extend, Stop }
public enum ProviderOperationStatus { Pending, InProgress, Succeeded, Failed, Unknown, Reconciling }

public sealed class ProviderOperation
{
    private ProviderOperation() { }
    public ProviderOperation(Guid id, Guid operationId, Guid? visitId, Guid? providerParkingActionId, ProviderOperationType type)
    { Id = id; OperationId = operationId; VisitId = visitId; ProviderParkingActionId = providerParkingActionId; Type = type; Status = ProviderOperationStatus.Pending; CreatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid? VisitId { get; private set; }
    public Guid? ProviderParkingActionId { get; private set; }
    public Guid? ParentOperationId { get; private set; }
    public ProviderOperationType Type { get; private set; }
    public ProviderOperationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AttemptStartedAt { get; private set; }
    public DateTimeOffset? RequestedEndAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public uint Version { get; private set; }
    public void SetParentOperationId(Guid parentOperationId)
    {
        if (parentOperationId == Guid.Empty)
            throw new ArgumentException("Parent operation id is required.", nameof(parentOperationId));
        if (ParentOperationId is not null && ParentOperationId != parentOperationId)
            throw new InvalidOperationException("Parent operation id cannot be changed once assigned.");
        ParentOperationId = parentOperationId;
    }

    public void AttachProviderParkingAction(Guid providerParkingActionId)
    {
        if (Type != ProviderOperationType.Stop || Status != ProviderOperationStatus.Pending || ProviderParkingActionId is not null)
            throw new InvalidOperationException("Only an unbound pending Stop operation can be attached to a provider action.");
        if (providerParkingActionId == Guid.Empty)
            throw new ArgumentException("Provider parking action id is required.", nameof(providerParkingActionId));
        ProviderParkingActionId = providerParkingActionId;
    }
    public void SetRequestedEndAt(DateTimeOffset requestedEndAt)
    {
        if (Type is not (ProviderOperationType.Extend or ProviderOperationType.ContinueStart) ||
            Status != ProviderOperationStatus.Pending)
            throw new InvalidOperationException("Requested end can only be set on a pending Extend or ContinueStart operation.");
        RequestedEndAt = requestedEndAt;
    }
    public void BeginAttempt() { if (Status is not ProviderOperationStatus.Pending) throw new InvalidOperationException("Only a pending operation can begin a provider attempt; unknown outcomes must reconcile first."); Status = ProviderOperationStatus.InProgress; AttemptCount++; AttemptStartedAt = DateTimeOffset.UtcNow; }
    public void MarkUnknown(string? errorCode = null) { if (Status != ProviderOperationStatus.InProgress) throw new InvalidOperationException(); LastErrorCode = errorCode; Status = ProviderOperationStatus.Unknown; }
    public void BeginReconciliation() { if (Status != ProviderOperationStatus.Unknown) throw new InvalidOperationException(); Status = ProviderOperationStatus.Reconciling; }
    public void ResumeUnknownAfterInterruptedReconciliation() { if (Status != ProviderOperationStatus.Reconciling) throw new InvalidOperationException("Only a reconciling operation can resume as Unknown after restart."); Status = ProviderOperationStatus.Unknown; }
    public void ResetForRetry() { if (Status != ProviderOperationStatus.Unknown) throw new InvalidOperationException("Only an unknown operation can be retried after reconciliation established no provider action exists."); Status = ProviderOperationStatus.Pending; LastErrorCode = null; }
    public void Succeed(DateTimeOffset completedAt) { if (Status is not ProviderOperationStatus.InProgress and not ProviderOperationStatus.Reconciling) throw new InvalidOperationException(); Status = ProviderOperationStatus.Succeeded; CompletedAt = completedAt; }
    public void Fail(string? errorCode, DateTimeOffset completedAt) { if (Status is not ProviderOperationStatus.InProgress and not ProviderOperationStatus.Reconciling) throw new InvalidOperationException(); LastErrorCode = errorCode; Status = ProviderOperationStatus.Failed; CompletedAt = completedAt; }
}
