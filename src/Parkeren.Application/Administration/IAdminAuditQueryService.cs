namespace Parkeren.Application.Administration;

public sealed record AdminAuditQuery(
    Guid? ActorUserId = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = 100);

public sealed record AdminAuditEventSummary(
    Guid Id,
    Guid ActorUserId,
    string ActorUsername,
    string Action,
    string TargetType,
    string? TargetId,
    DateTimeOffset CreatedAt,
    string? ContextJson);

public interface IAdminAuditQueryService
{
    Task<IReadOnlyList<AdminAuditEventSummary>> GetEventsAsync(
        Guid actorUserId,
        AdminAuditQuery query,
        CancellationToken cancellationToken = default);
}
