namespace Parkeren.Application.Administration;

public interface IAdminAuditWriter
{
    Task WriteAsync(
        Guid actorUserId,
        string action,
        string targetType,
        string? targetId,
        object? context,
        CancellationToken cancellationToken = default);
}
