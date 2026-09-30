using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Parkeren.Infrastructure.Persistence;
using WebPush;

namespace Parkeren.Infrastructure.Notifications;

public sealed class WebPushSender(
    ParkerenDbContext dbContext,
    IConfiguration configuration,
    ILogger<WebPushSender> logger)
{
    public async Task SendAsync(
        Guid recipientUserId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var subject = configuration["WebPush:Subject"];
        var publicKey = configuration["WebPush:PublicKey"];
        var privateKey = configuration["WebPush:PrivateKey"];

        if (string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(publicKey) ||
            string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("Web Push delivery skipped because VAPID is not fully configured.");
            return;
        }

        var subscriptions = await dbContext.PushSubscriptions
            .Where(x => x.UserId == recipientUserId)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
            return;

        var client = new WebPushClient();
        var vapid = new VapidDetails(subject, publicKey, privateKey);

        foreach (var stored in subscriptions)
        {
            try
            {
                var subscription = new WebPush.PushSubscription(
                    stored.Endpoint,
                    stored.P256dh,
                    stored.Auth);

                await client.SendNotificationAsync(
                    subscription,
                    payload,
                    vapid,
                    cancellationToken);
            }
            catch (WebPushException exception) when (
                exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                logger.LogInformation(
                    "Removing expired Web Push subscription {PushSubscriptionId} for user {UserId}.",
                    stored.Id,
                    recipientUserId);
                dbContext.PushSubscriptions.Remove(stored);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Web Push delivery failed for subscription {PushSubscriptionId} and user {UserId}.",
                    stored.Id,
                    recipientUserId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
