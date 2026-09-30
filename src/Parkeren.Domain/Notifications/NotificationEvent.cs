namespace Parkeren.Domain.Notifications;

public enum NotificationEventType
{
    VisitStarted,
    VisitStopped,
    ProviderContinuationSucceeded,
    ProviderContinuationAttentionRequired
}

public sealed class NotificationEvent
{
    private NotificationEvent() { }

    public NotificationEvent(Guid id, NotificationEventType type, Guid aggregateId, DateTimeOffset occurredAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Notification event id is required.", nameof(id));
        if (aggregateId == Guid.Empty) throw new ArgumentException("Aggregate id is required.", nameof(aggregateId));

        Id = id;
        Type = type;
        AggregateId = aggregateId;
        OccurredAt = occurredAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public NotificationEventType Type { get; private set; }
    public Guid AggregateId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
