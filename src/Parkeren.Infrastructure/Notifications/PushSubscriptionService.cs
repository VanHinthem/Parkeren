using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Notifications;

public enum PushSubscriptionRegistrationResult
{
    Created,
    AlreadyRegistered,
    EndpointOwnedByAnotherUser
}

public sealed class PushSubscriptionService(ParkerenDbContext dbContext)
{
    public async Task<PushSubscriptionRegistrationResult> RegisterAsync(
        Guid userId,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = endpoint.Trim();
        var existing = await dbContext.PushSubscriptions
            .SingleOrDefaultAsync(x => x.Endpoint == normalizedEndpoint, cancellationToken);

        if (existing is not null)
        {
            if (existing.UserId != userId)
                return PushSubscriptionRegistrationResult.EndpointOwnedByAnotherUser;

            if (existing.P256dh == p256dh.Trim() && existing.Auth == auth.Trim())
                return PushSubscriptionRegistrationResult.AlreadyRegistered;

            existing.UpdateKeys(p256dh, auth);
            await dbContext.SaveChangesAsync(cancellationToken);
            return PushSubscriptionRegistrationResult.AlreadyRegistered;
        }

        dbContext.PushSubscriptions.Add(new PushSubscription(
            Guid.NewGuid(),
            userId,
            normalizedEndpoint,
            p256dh,
            auth,
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);
        return PushSubscriptionRegistrationResult.Created;
    }

    public async Task RemoveAsync(
        Guid userId,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = endpoint.Trim();
        var subscription = await dbContext.PushSubscriptions
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.Endpoint == normalizedEndpoint,
                cancellationToken);

        if (subscription is null)
            return;

        dbContext.PushSubscriptions.Remove(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
