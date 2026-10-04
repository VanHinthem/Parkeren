using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Administration;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Administration;

internal sealed class AdminAuditQueryService(ParkerenDbContext dbContext) : IAdminAuditQueryService
{
    public async Task<IReadOnlyList<AdminAuditEventSummary>> GetEventsAsync(
        Guid actorUserId,
        AdminAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await dbContext.Users.AsNoTracking().AnyAsync(
            x => x.Id == actorUserId && x.Status == UserStatus.Active && x.Role == UserRole.Admin,
                cancellationToken))
            throw new UnauthorizedAccessException("Active administrator required.");

        if (query.From.HasValue && query.To.HasValue && query.To.Value < query.From.Value)
            throw new ArgumentException("To must be on or after from.", nameof(query));

        var events =
            from audit in dbContext.AdminAuditEvents.AsNoTracking()
            join actor in dbContext.Users.AsNoTracking() on audit.ActorUserId equals actor.Id
            select new
            {
                Audit = audit,
                ActorUsername = actor.Username
            };

        if (query.ActorUserId.HasValue)
            events = events.Where(x => x.Audit.ActorUserId == query.ActorUserId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            events = events.Where(x => x.Audit.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            var targetType = query.TargetType.Trim();
            events = events.Where(x => x.Audit.TargetType == targetType);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetId))
        {
            var targetId = query.TargetId.Trim();
            events = events.Where(x => x.Audit.TargetId == targetId);
        }

        if (query.From.HasValue)
            events = events.Where(x => x.Audit.CreatedAt >= query.From.Value);
        if (query.To.HasValue)
            events = events.Where(x => x.Audit.CreatedAt <= query.To.Value);

        var limit = Math.Clamp(query.Limit, 1, 200);
        return await events
            .OrderByDescending(x => x.Audit.CreatedAt)
            .ThenByDescending(x => x.Audit.Id)
            .Take(limit)
            .Select(x => new AdminAuditEventSummary(
                x.Audit.Id,
                x.Audit.ActorUserId,
                x.ActorUsername,
                x.Audit.Action,
                x.Audit.TargetType,
                x.Audit.TargetId,
                x.Audit.CreatedAt,
                x.Audit.ContextJson))
            .ToListAsync(cancellationToken);
    }
}
