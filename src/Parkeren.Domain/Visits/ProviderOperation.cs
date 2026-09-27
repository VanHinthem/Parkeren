namespace Parkeren.Domain.Visits;

public enum ProviderOperationType { Start, Extend, Stop }
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
    public ProviderOperationType Type { get; private set; }
    public ProviderOperationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public uint Version { get; private set; }
    public void BeginAttempt() { if (Status is not ProviderOperationStatus.Pending) throw new InvalidOperationException("Only a pending operation can begin a provider attempt; unknown outcomes must reconcile first."); Status = ProviderOperationStatus.InProgress; AttemptCount++; }
    public void MarkUnknown(string? errorCode = null) { if (Status != ProviderOperationStatus.InProgress) throw new InvalidOperationException(); LastErrorCode = errorCode; Status = ProviderOperationStatus.Unknown; }
    public void BeginReconciliation() { if (Status != ProviderOperationStatus.Unknown) throw new InvalidOperationException(); Status = ProviderOperationStatus.Reconciling; }
    public void ResetForRetry() { if (Status != ProviderOperationStatus.Unknown) throw new InvalidOperationException("Only an unknown operation can be retried after reconciliation established no provider action exists."); Status = ProviderOperationStatus.Pending; LastErrorCode = null; }
    public void Succeed(DateTimeOffset completedAt) { if (Status is not ProviderOperationStatus.InProgress and not ProviderOperationStatus.Reconciling) throw new InvalidOperationException(); Status = ProviderOperationStatus.Succeeded; CompletedAt = completedAt; }
    public void Fail(string? errorCode, DateTimeOffset completedAt) { if (Status is not ProviderOperationStatus.InProgress and not ProviderOperationStatus.Reconciling) throw new InvalidOperationException(); LastErrorCode = errorCode; Status = ProviderOperationStatus.Failed; CompletedAt = completedAt; }
}
