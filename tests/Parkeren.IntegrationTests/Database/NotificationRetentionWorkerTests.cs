using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Parkeren.Api;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class NotificationRetentionWorkerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Retention_cleanup_deletes_only_notifications_older_than_cutoff()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User(Guid.NewGuid(), $"retention-{suffix}", $"RETENTION-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"R{suffix[..7]}", $"R{suffix[..7]}", null);
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddDays(-100),
            null,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        var auditEvent = new VisitSchedulerAuditEvent(
            Guid.NewGuid(),
            visit.Id,
            now.AddDays(-100),
            1,
            "retention-test",
            Guid.NewGuid(),
            "VisitCreated",
            $"retention:{suffix}",
            null,
            "test",
            null,
            $"retention:{suffix}:visit-created");
        var expiredNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-91));
        var cutoffNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-90));
        var recentNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-30));
        var configuredExpiredNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-31));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.VisitSchedulerAuditEvents.Add(auditEvent);
            seed.Notifications.AddRange(expiredNotification, cutoffNotification, recentNotification);
            await seed.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
            });
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            await using var serviceProvider = services.BuildServiceProvider();
            var worker = new NotificationRetentionWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new NotificationRetentionOptions()),
                new FixedTimeProvider(now),
                NullLogger<NotificationRetentionWorker>.Instance);

            var deleted = await worker.DeleteExpiredNotificationsAsync(cancellationToken);

            Assert.Equal(1, deleted);
            await using var verify = fixture.CreateDbContext();
            Assert.False(await verify.Notifications.AnyAsync(x => x.Id == expiredNotification.Id, cancellationToken));
            Assert.True(await verify.Notifications.AnyAsync(x => x.Id == cutoffNotification.Id, cancellationToken));
            Assert.True(await verify.Notifications.AnyAsync(x => x.Id == recentNotification.Id, cancellationToken));
            Assert.True(await verify.VisitSchedulerAuditEvents.AnyAsync(x => x.Id == auditEvent.Id, cancellationToken));

            verify.Notifications.Add(configuredExpiredNotification);
            await verify.SaveChangesAsync(cancellationToken);

            var configuredWorker = new NotificationRetentionWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new NotificationRetentionOptions { RetentionDays = 30 }),
                new FixedTimeProvider(now),
                NullLogger<NotificationRetentionWorker>.Instance);

            Assert.Equal(2, await configuredWorker.DeleteExpiredNotificationsAsync(cancellationToken));
            await using var configuredVerify = fixture.CreateDbContext();
            Assert.False(await configuredVerify.Notifications.AnyAsync(x => x.Id == cutoffNotification.Id, cancellationToken));
            Assert.False(await configuredVerify.Notifications.AnyAsync(x => x.Id == configuredExpiredNotification.Id, cancellationToken));
            Assert.True(await configuredVerify.Notifications.AnyAsync(x => x.Id == recentNotification.Id, cancellationToken));
            Assert.True(await configuredVerify.VisitSchedulerAuditEvents.AnyAsync(x => x.Id == auditEvent.Id, cancellationToken));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(cancellationToken, visit.Id);
            await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Notifications.Where(x => x.RecipientUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}