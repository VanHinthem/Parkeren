using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Parkeren.Application.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Notifications;
using WebPush;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class PushDeliveryProcessorTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Delivered_result_marks_delivery_delivered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearPushDeliveriesAsync(cancellationToken);
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
        await ClearPushDeliveriesAsync(cancellationToken);
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.RetryRequired), timeProvider);

        await processor.ProcessNextAsync(cancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await processor.ProcessNextAsync(cancellationToken);

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
    }

    [Fact]
    public async Task Third_retry_required_marks_delivery_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearPushDeliveriesAsync(cancellationToken);
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.RetryRequired), timeProvider);

        await processor.ProcessNextAsync(cancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await processor.ProcessNextAsync(cancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await processor.ProcessNextAsync(cancellationToken);

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public async Task Not_configured_marks_delivery_failed_without_retry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearPushDeliveriesAsync(cancellationToken);
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        await using var context = fixture.CreateDbContext();
        var processor = CreateProcessor(context, new FakeSender(WebPushSendResult.NotConfigured));

        await processor.ProcessNextAsync(cancellationToken);

        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public async Task Sender_exception_details_are_not_written_to_processor_logs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearPushDeliveriesAsync(cancellationToken);
        var deliveryId = await CreateDeliveryAsync(cancellationToken);
        const string sensitiveDetails = "canary-sensitive-push-details";
        await using var context = fixture.CreateDbContext();
        var logger = new CapturingLogger<PushDeliveryProcessor>();
        var processor = new PushDeliveryProcessor(
            context,
            new ThrowingSender(sensitiveDetails),
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            logger);

        Assert.True(await processor.ProcessNextAsync(cancellationToken));

        Assert.Contains($"Push delivery {deliveryId} failed.", logger.Entries);
        Assert.All(logger.Exceptions, exception => Assert.Null(exception));
        Assert.DoesNotContain(sensitiveDetails, string.Join(Environment.NewLine, logger.Entries), StringComparison.Ordinal);
        var delivery = await context.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId, cancellationToken);
        Assert.Equal(PushDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public async Task Sender_exception_details_are_not_written_to_sender_logs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"sender-{suffix}", $"SENDER-{suffix}", "hash", UserRole.Visitor);
        var subscriptionId = Guid.NewGuid();
        const string sensitiveDetails = "canary-sensitive-webpush-details";
        var keys = VapidHelper.GenerateVapidKeys();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.PushSubscriptions.Add(new Parkeren.Domain.Notifications.PushSubscription(
                subscriptionId,
                user.Id,
                $"https://127.0.0.1:1/{sensitiveDetails}",
                "invalid-p256dh",
                "invalid-auth",
                DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebPush:Subject"] = "mailto:parkeren@example.test",
                ["WebPush:PublicKey"] = keys.PublicKey,
                ["WebPush:PrivateKey"] = keys.PrivateKey
            });
            await using var context = fixture.CreateDbContext();
            var logger = new CapturingLogger<WebPushSender>();
            var sender = new WebPushSender(context, configuration, logger);

            var result = await sender.SendAsync(user.Id, "payload", cancellationToken);

            Assert.Equal(WebPushSendResult.RetryRequired, result);
            Assert.Contains(
                logger.Entries,
                entry => entry.Contains(
                    $"Web Push delivery failed for subscription {subscriptionId} and user {user.Id}.",
                    StringComparison.Ordinal)
                    && entry.Contains("Failure type:", StringComparison.Ordinal));
            Assert.All(logger.Exceptions, exception => Assert.Null(exception));
            Assert.DoesNotContain(sensitiveDetails, string.Join(Environment.NewLine, logger.Entries), StringComparison.Ordinal);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.PushSubscriptions.Where(x => x.Id == subscriptionId).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Push_failure_after_successful_stop_keeps_visit_and_notification_completed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearPushDeliveriesAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"stop-push-{suffix}", $"STOP-PUSH-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SP{suffix[..6]}", $"SP{suffix[..6]}", null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, startAt.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            await seed.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        StopVisitClaim claim;
        await using (var scope = provider.CreateAsyncScope())
        {
            claim = await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), cancellationToken);
        }

        var actualEndAt = DateTimeOffset.UtcNow;
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStopVisitFinalizer>()
                .CompleteWithoutProviderActionAsync(claim, actualEndAt, cancellationToken);
        }

        Guid notificationId;
        Guid deliveryId;
        await using (var verify = fixture.CreateDbContext())
        {
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
            var notification = await verify.Notifications.SingleAsync(
                x => x.VisitId == visit.Id && x.Type == NotificationType.VisitStopped,
                cancellationToken);
            var delivery = await verify.PushDeliveries.SingleAsync(
                x => x.NotificationId == notification.Id,
                cancellationToken);

            Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
            notificationId = notification.Id;
            deliveryId = delivery.Id;
        }

        await using (var deliveryContext = fixture.CreateDbContext())
        {
            var processor = CreateProcessor(
                deliveryContext,
                new FakeSender(WebPushSendResult.NotConfigured));
            Assert.True(await processor.ProcessNextAsync(cancellationToken));
        }

        await using (var verify = fixture.CreateDbContext())
        {
            Assert.Equal(
                VisitStatus.Completed,
                (await verify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Status);
            Assert.True(await verify.Notifications.AnyAsync(x => x.Id == notificationId, cancellationToken));
            var delivery = await verify.PushDeliveries.SingleAsync(x => x.Id == deliveryId, cancellationToken);
            Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        }

        await using var cleanup = fixture.CreateDbContext();
        await cleanup.PushDeliveries.Where(x => x.Id == deliveryId).ExecuteDeleteAsync(cancellationToken);
        await cleanup.Notifications.Where(x => x.Id == notificationId).ExecuteDeleteAsync(cancellationToken);
        await cleanup.NotificationEvents.Where(x => x.AggregateId == visit.Id).ExecuteDeleteAsync(cancellationToken);
        await cleanup.ProviderOperations.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(cancellationToken);
        await cleanup.VisitSchedulerWork.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(cancellationToken);
        await cleanup.DeleteVisitSchedulerAuditEventsAsync(cancellationToken, visit.Id);
        await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(cancellationToken);
        await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(cancellationToken);
        await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
    }


    private async Task ClearPushDeliveriesAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.PushDeliveries.ExecuteDeleteAsync(cancellationToken);
        await context.Notifications.ExecuteDeleteAsync(cancellationToken);
    }

    private static PushDeliveryProcessor CreateProcessor(
        Infrastructure.Persistence.ParkerenDbContext context,
        IWebPushSender sender,
        TimeProvider? timeProvider = null) =>
        new(context, sender, timeProvider ?? TimeProvider.System, NullLogger<PushDeliveryProcessor>.Instance);

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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class FakeSender(WebPushSendResult result) : IWebPushSender
    {
        public Task<WebPushSendResult> SendAsync(
            Guid recipientUserId,
            string payload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingSender(string sensitiveDetails) : IWebPushSender
    {
        public Task<WebPushSendResult> SendAsync(
            Guid recipientUserId,
            string payload,
            CancellationToken cancellationToken = default) =>
            Task.FromException<WebPushSendResult>(new InvalidOperationException(sensitiveDetails));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];
        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
            Exceptions.Add(exception);
            if (exception is not null)
                Entries.Add(exception.ToString());
        }
    }
}
