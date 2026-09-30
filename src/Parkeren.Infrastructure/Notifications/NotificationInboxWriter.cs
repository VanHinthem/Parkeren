using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Notifications;

internal sealed class NotificationInboxWriter(ParkerenDbContext dbContext)
{
    public async Task WriteAsync(
        NotificationEvent notificationEvent,
        NotificationType type,
        Guid visitUserId,
        bool includeVisitor,
        bool includeAdmins,
        CancellationToken cancellationToken = default,
        string? payload = null,
        Guid? visitId = null)
    {
        var recipientIds = new List<Guid>();
        if (includeVisitor)
            recipientIds.Add(visitUserId);

        if (includeAdmins)
        {
            recipientIds.AddRange(await dbContext.Users
                .Where(x => x.IsActive && x.Role == UserRole.Admin)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken));
        }

        recipientIds = recipientIds.Distinct().ToList();
        if (recipientIds.Count == 0)
            return;

        var existing = await dbContext.Notifications
            .Where(x => x.SourceEventId == notificationEvent.Id && recipientIds.Contains(x.RecipientUserId))
            .Select(x => x.RecipientUserId)
            .ToListAsync(cancellationToken);

        foreach (var recipientId in recipientIds.Except(existing))
        {
            dbContext.Notifications.Add(new Notification(
                Guid.NewGuid(),
                recipientId,
                type,
                notificationEvent.OccurredAt,
                visitId ?? notificationEvent.AggregateId,
                notificationEvent.Id,
                payload));
        }
    }
}
