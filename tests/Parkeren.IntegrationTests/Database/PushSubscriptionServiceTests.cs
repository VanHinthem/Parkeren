using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Notifications;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class PushSubscriptionServiceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Register_is_idempotent_for_same_user_and_endpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync(cancellationToken);
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        var service = new PushSubscriptionService(context);

        Assert.Equal(PushSubscriptionRegistrationResult.Created,
            await service.RegisterAsync(user.Id, endpoint, "p256dh", "auth", cancellationToken));
        Assert.Equal(PushSubscriptionRegistrationResult.AlreadyRegistered,
            await service.RegisterAsync(user.Id, endpoint, "p256dh", "auth", cancellationToken));

        Assert.Equal(1, await context.PushSubscriptions.CountAsync(x => x.Endpoint == endpoint, cancellationToken));
    }

    [Fact]
    public async Task Register_refreshes_keys_for_existing_endpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync(cancellationToken);
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        var service = new PushSubscriptionService(context);
        await service.RegisterAsync(user.Id, endpoint, "old-p256dh", "old-auth", cancellationToken);

        var result = await service.RegisterAsync(user.Id, endpoint, "new-p256dh", "new-auth", cancellationToken);

        Assert.Equal(PushSubscriptionRegistrationResult.AlreadyRegistered, result);
        await using var verifyContext = fixture.CreateDbContext();
        var subscription = await verifyContext.PushSubscriptions.SingleAsync(x => x.Endpoint == endpoint, cancellationToken);
        Assert.Equal("new-p256dh", subscription.P256dh);
        Assert.Equal("new-auth", subscription.Auth);
    }

    [Fact]
    public async Task Register_rejects_endpoint_owned_by_another_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstUser = await CreateUserAsync(cancellationToken);
        var secondUser = await CreateUserAsync(cancellationToken);
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        var service = new PushSubscriptionService(context);

        await service.RegisterAsync(firstUser.Id, endpoint, "p256dh", "auth", cancellationToken);
        var result = await service.RegisterAsync(secondUser.Id, endpoint, "other-p256dh", "other-auth", cancellationToken);

        Assert.Equal(PushSubscriptionRegistrationResult.EndpointOwnedByAnotherUser, result);
        var subscription = await context.PushSubscriptions.SingleAsync(x => x.Endpoint == endpoint, cancellationToken);
        Assert.Equal(firstUser.Id, subscription.UserId);
    }

    [Fact]
    public async Task Remove_only_removes_subscription_owned_by_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await CreateUserAsync(cancellationToken);
        var otherUser = await CreateUserAsync(cancellationToken);
        var endpoint = $"https://push.example.test/{Guid.NewGuid():N}";

        await using var context = fixture.CreateDbContext();
        var service = new PushSubscriptionService(context);
        await service.RegisterAsync(owner.Id, endpoint, "p256dh", "auth", cancellationToken);

        await service.RemoveAsync(otherUser.Id, endpoint, cancellationToken);
        Assert.True(await context.PushSubscriptions.AnyAsync(x => x.Endpoint == endpoint, cancellationToken));

        await service.RemoveAsync(owner.Id, endpoint, cancellationToken);
        Assert.False(await context.PushSubscriptions.AnyAsync(x => x.Endpoint == endpoint, cancellationToken));
    }

    private async Task<User> CreateUserAsync(CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"push-{suffix}", $"PUSH-{suffix}", "hash", UserRole.Visitor);
        await using var context = fixture.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        return user;
    }
}
