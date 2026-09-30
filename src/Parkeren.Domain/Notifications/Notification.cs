namespace Parkeren.Domain.Notifications;

public enum NotificationType
{
    VisitStarted,
    VisitStopped,
    ProviderContinuationSucceeded,
    ProviderContinuationAttentionRequired,
    LongVisitWarning,
    BudgetWarning
}

public sealed class Notification
{
    private Notification() { }

    public Notification(
        Guid id,
        Guid recipientUserId,
        NotificationType type,
        DateTimeOffset createdAt,
        Guid? visitId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Notification id is required.", nameof(id));
        if (recipientUserId == Guid.Empty) throw new ArgumentException("Recipient user id is required.", nameof(recipientUserId));

        Id = id;
        RecipientUserId = recipientUserId;
        Type = type;
        VisitId = visitId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public NotificationType Type { get; private set; }
    public Guid? VisitId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt.HasValue;

    public void MarkRead(DateTimeOffset readAt)
    {
        if (!ReadAt.HasValue)
            ReadAt = readAt;
    }
}
