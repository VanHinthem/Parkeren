namespace Parkeren.Domain.Visits;

public enum VisitSchedulerWorkType
{
    ContinueProviderCoverage,
    StopVisit,
    LongVisitWarning,
    ReconcileProviderAction
}

public enum VisitSchedulerWorkStatus
{
    Pending,
    Claimed,
    Completed,
    Cancelled
}

public sealed class VisitSchedulerWork
{
    private VisitSchedulerWork() { }

    public VisitSchedulerWork(
        Guid id,
        Guid visitId,
        VisitSchedulerWorkType type,
        DateTimeOffset dueAt,
        VisitEndReason? endReason = null,
        Guid? providerParkingActionId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Scheduler work id is required.", nameof(id));
        if (visitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(visitId));
        if (endReason is not null && type != VisitSchedulerWorkType.StopVisit)
            throw new ArgumentException("Only StopVisit scheduler work can carry a Visit end reason.", nameof(endReason));
        if (type == VisitSchedulerWorkType.ReconcileProviderAction &&
            (!providerParkingActionId.HasValue || providerParkingActionId == Guid.Empty))
            throw new ArgumentException("Provider-action reconciliation work requires a provider action id.", nameof(providerParkingActionId));
        if (type != VisitSchedulerWorkType.ReconcileProviderAction && providerParkingActionId is not null)
            throw new ArgumentException("Only provider-action reconciliation work can reference a provider action.", nameof(providerParkingActionId));

        Id = id;
        VisitId = visitId;
        Type = type;
        DueAt = dueAt;
        EndReason = endReason;
        ProviderParkingActionId = providerParkingActionId;
        Status = VisitSchedulerWorkStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid VisitId { get; private set; }
    public VisitSchedulerWorkType Type { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public VisitEndReason? EndReason { get; private set; }
    public Guid? ProviderParkingActionId { get; private set; }
    public int AttemptCount { get; private set; }
    public VisitSchedulerWorkStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public string? ClaimedBy { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public uint Version { get; private set; }

    public void Claim(string workerId, DateTimeOffset claimedAt)
    {
        if (Status != VisitSchedulerWorkStatus.Pending)
            throw new InvalidOperationException("Only pending scheduler work can be claimed.");
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker id is required.", nameof(workerId));

        Status = VisitSchedulerWorkStatus.Claimed;
        ClaimedBy = workerId;
        ClaimedAt = claimedAt;
        AttemptCount++;
    }

    public void Defer(DateTimeOffset dueAt)
    {
        if (Status != VisitSchedulerWorkStatus.Pending)
            throw new InvalidOperationException("Only pending scheduler work can be deferred.");
        if (dueAt <= DueAt)
            throw new ArgumentOutOfRangeException(nameof(dueAt), "Deferred scheduler work must move to a later due time.");

        DueAt = dueAt;
    }

    public void Release(DateTimeOffset dueAt)
    {
        if (Status != VisitSchedulerWorkStatus.Claimed)
            throw new InvalidOperationException("Only claimed scheduler work can be released.");
        if (dueAt <= ClaimedAt)
            throw new ArgumentOutOfRangeException(nameof(dueAt), "Released scheduler work must be due after it was claimed.");

        Status = VisitSchedulerWorkStatus.Pending;
        DueAt = dueAt;
        ClaimedAt = null;
        ClaimedBy = null;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status != VisitSchedulerWorkStatus.Claimed)
            throw new InvalidOperationException("Only claimed scheduler work can be completed.");

        Status = VisitSchedulerWorkStatus.Completed;
        CompletedAt = completedAt;
    }

    public void Cancel()
    {
        if (Status is VisitSchedulerWorkStatus.Completed or VisitSchedulerWorkStatus.Cancelled)
            throw new InvalidOperationException("Completed or cancelled scheduler work cannot be cancelled again.");

        Status = VisitSchedulerWorkStatus.Cancelled;
    }
}
