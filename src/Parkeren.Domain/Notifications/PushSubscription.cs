namespace Parkeren.Domain.Notifications;

public sealed class PushSubscription
{
    private PushSubscription() { }

    public PushSubscription(
        Guid id,
        Guid userId,
        string endpoint,
        string p256dh,
        string auth,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Push subscription id is required.", nameof(id));
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Push endpoint is required.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(p256dh)) throw new ArgumentException("Push p256dh key is required.", nameof(p256dh));
        if (string.IsNullOrWhiteSpace(auth)) throw new ArgumentException("Push auth key is required.", nameof(auth));

        Id = id;
        UserId = userId;
        Endpoint = endpoint.Trim();
        P256dh = p256dh.Trim();
        Auth = auth.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string P256dh { get; private set; } = string.Empty;
    public string Auth { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
