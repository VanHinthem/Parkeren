namespace Parkeren.Domain.Notifications;

public enum PushDeliveryStatus
{
    Pending,
    Delivered,
    Failed
}

public sealed class PushDelivery
{
    private PushDelivery() { }

    public PushDelivery(Guid id, Guid notificationId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Push delivery id is required.", nameof(id));
        if (notificationId == Guid.Empty) throw new ArgumentException("Notification id is required.", nameof(notificationId));

        Id = id;
        NotificationId = notificationId;
        Status = PushDeliveryStatus.Pending;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public PushDeliveryStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int AttemptCount { get; private set; }

    public void MarkAttempt(DateTimeOffset attemptedAt)
    {
        LastAttemptAt = attemptedAt;
        AttemptCount++;
    }

    public void MarkDelivered(DateTimeOffset deliveredAt)
    {
        Status = PushDeliveryStatus.Delivered;
        DeliveredAt = deliveredAt;
    }

    public void MarkFailed() => Status = PushDeliveryStatus.Failed;
}
