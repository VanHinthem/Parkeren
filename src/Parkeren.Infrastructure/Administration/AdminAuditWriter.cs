using System.Text.Json;
using Parkeren.Application.Administration;
using Parkeren.Domain.Administration;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Administration;

internal sealed class AdminAuditWriter(
    ParkerenDbContext dbContext,
    TimeProvider timeProvider) : IAdminAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(
        Guid actorUserId,
        string action,
        string targetType,
        string? targetId,
        object? context,
        CancellationToken cancellationToken = default)
    {
        var contextJson = context is null
            ? null
            : JsonSerializer.Serialize(context, JsonOptions);

        dbContext.AdminAuditEvents.Add(new AdminAuditEvent(
            Guid.NewGuid(),
            actorUserId,
            action,
            targetType,
            targetId,
            timeProvider.GetUtcNow(),
            contextJson));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
