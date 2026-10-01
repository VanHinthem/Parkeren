namespace Parkeren.Domain.Administration;

public sealed class AdminAuditEvent
{
    private AdminAuditEvent() { }

    public AdminAuditEvent(
        Guid id,
        Guid actorUserId,
        string action,
        string targetType,
        string? targetId,
        DateTimeOffset createdAt,
        string? contextJson = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Audit event id is required.", nameof(id));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(targetType)) throw new ArgumentException("Target type is required.", nameof(targetType));

        Id = id;
        ActorUserId = actorUserId;
        Action = action.Trim();
        TargetType = targetType.Trim();
        TargetId = string.IsNullOrWhiteSpace(targetId) ? null : targetId.Trim();
        CreatedAt = createdAt;
        ContextJson = string.IsNullOrWhiteSpace(contextJson) ? null : contextJson;
    }

    public Guid Id { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string? TargetId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ContextJson { get; private set; }
}
