using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Notifications;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class PushDeliveryProcessorTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Delivered_result_marks_delivery_delivered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.Delivered));

        Assert.True(await processor.ProcessNextAsync(cancellationToken));

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.NotNull(delivery.DeliveredAt);
    }

    [Fact]
    public async Task Retry_required_stays_pending_before_third_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.RetryRequired));

        await processor.ProcessNextAsync(cancellationToken);
        await processor.ProcessNextAsync(cancellationToken);

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
    }

    [Fact]
    public async Task Third_retry_required_marks_delivery_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.RetryRequired));

        await processor.ProcessNextAsync(cancellationToken);
        await processor.ProcessNextAsync(cancellationToken);
        await processor.ProcessNextAsync(cancellationToken);

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public async Task Not_configured_marks_delivery_failed_without_retry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.NotConfigured));

        await processor.ProcessNextAsync(cancellationToken);

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
    }

    private static PushDeliveryProcessor CreateProcessor(
        Infrastructure.Persistence.ParkerenDbContext context,
        IWebPushSender sender) =>
        new(context, sender, TimeProvider.System, NullLogger<PushDeliveryProcessor>.Instance);

    private async Task<Guid> CreateDeliveryAsync(CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"delivery-{suffix}", $"DEL-{suffix}", "hash", UserRole.Visitor);
        var notification = new Notification(
            Guid.NewGuid(),
            user.Id,
            NotificationType.VisitStarted,
            DateTimeOffset.UtcNow);
        var delivery = new PushDelivery(Guid.NewGuid(), notification.Id, DateTimeOffset.UtcNow);

        await using var context = fixture.CreateDbContext();
        context.Users.Add(user);
        context.Notifications.Add(notification);
        context.PushDeliveries.Add(delivery);
        await context.SaveChangesAsync(cancellationToken);
        return delivery.Id;
    }

    private sealed class FakeSender(WebPushSendResult result) : IWebPushSender
    {
        public Task<WebPushSendResult> SendAsync(
            Guid recipientUserId,
            string payload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
