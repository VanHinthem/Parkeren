using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Notifications;

internal sealed class StartVisitNotificationPublisher(ParkerenDbContext dbContext) : IStartVisitNotificationPublisher
{
    public async Task PublishStartedAsync(Visit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (visit.Status != VisitStatus.Active)
            throw new InvalidOperationException("A VisitStarted notification event can only be recorded for an active Visit.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var notificationEvent = await dbContext.NotificationEvents
            .SingleOrDefaultAsync(
                x => x.Type == NotificationEventType.VisitStarted && x.AggregateId == visit.Id,
                cancellationToken);

        if (notificationEvent is null)
        {
            notificationEvent = new NotificationEvent(
                Guid.NewGuid(),
                NotificationEventType.VisitStarted,
                visit.Id,
                DateTimeOffset.UtcNow);
            dbContext.NotificationEvents.Add(notificationEvent);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var recipientIds = await dbContext.Users
            .Where(x => x.IsActive && (x.Id == visit.UserId || x.Role == UserRole.Admin))
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var existingRecipientIds = await dbContext.Notifications
            .Where(x => x.SourceEventId == notificationEvent.Id && recipientIds.Contains(x.RecipientUserId))
            .Select(x => x.RecipientUserId)
            .ToListAsync(cancellationToken);

        foreach (var recipientId in recipientIds.Except(existingRecipientIds))
        {
            dbContext.Notifications.Add(new Notification(
                Guid.NewGuid(),
                recipientId,
                NotificationType.VisitStarted,
                notificationEvent.OccurredAt,
                visit.Id,
                notificationEvent.Id));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
