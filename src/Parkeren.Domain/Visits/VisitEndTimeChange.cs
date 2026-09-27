namespace Parkeren.Domain.Visits;

public enum VisitEndTimeChangeResult
{
    Pending,
    Applied,
    Rejected
}

public sealed class VisitEndTimeChange
{
    private VisitEndTimeChange() { }

    public VisitEndTimeChange(
        Guid id,
        Guid operationId,
        Guid visitId,
        Guid actorUserId,
        DateTimeOffset? previousDesiredEndAt,
        DateTimeOffset? requestedDesiredEndAt,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(operationId));
        if (visitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(visitId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));

        Id = id;
        OperationId = operationId;
        VisitId = visitId;
        ActorUserId = actorUserId;
        PreviousDesiredEndAt = previousDesiredEndAt;
        RequestedDesiredEndAt = requestedDesiredEndAt;
        CreatedAt = createdAt;
        Result = VisitEndTimeChangeResult.Pending;
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid VisitId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset? PreviousDesiredEndAt { get; private set; }
    public DateTimeOffset? RequestedDesiredEndAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public VisitEndTimeChangeResult Result { get; private set; }

    public void MarkApplied()
    {
        EnsurePending();
        Result = VisitEndTimeChangeResult.Applied;
    }

    public void MarkRejected()
    {
        EnsurePending();
        Result = VisitEndTimeChangeResult.Rejected;
    }

    private void EnsurePending()
    {
        if (Result != VisitEndTimeChangeResult.Pending)
            throw new InvalidOperationException("Only a pending end-time change can be completed.");
    }
}

