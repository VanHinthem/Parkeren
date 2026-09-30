using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Notifications;

public sealed class PushDeliveryProcessor(
    ParkerenDbContext dbContext,
    WebPushSender sender,
    TimeProvider timeProvider,
    ILogger<PushDeliveryProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var delivery = await dbContext.PushDeliveries
            .Where(x => x.Status == PushDeliveryStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null)
            return false;

        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(x => x.Id == delivery.NotificationId, cancellationToken);

        if (notification is null)
        {
            delivery.MarkFailed();
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var message = WebPushMessageFactory.Create(notification);
        if (message is null)
        {
            delivery.MarkFailed();
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = timeProvider.GetUtcNow();
        delivery.MarkAttempt(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await sender.SendAsync(
                notification.RecipientUserId,
                WebPushMessageFactory.Serialize(message),
                cancellationToken);

            delivery.MarkDelivered(timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Push delivery {PushDeliveryId} failed.", delivery.Id);
            if (delivery.AttemptCount >= 3)
                delivery.MarkFailed();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
