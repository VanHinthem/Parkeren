using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Parkeren.Api;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Users;
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
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var expiredNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-91));
        var cutoffNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-90));
        var recentNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddDays(-30));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
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
                Options.Create(new NotificationRetentionOptions { RetentionDays = 90 }),
                new FixedTimeProvider(now),
                NullLogger<NotificationRetentionWorker>.Instance);

            var deleted = await worker.DeleteExpiredNotificationsAsync(cancellationToken);

            Assert.Equal(1, deleted);
            await using var verify = fixture.CreateDbContext();
            Assert.False(await verify.Notifications.AnyAsync(x => x.Id == expiredNotification.Id, cancellationToken));
            Assert.True(await verify.Notifications.AnyAsync(x => x.Id == cutoffNotification.Id, cancellationToken));
            Assert.True(await verify.Notifications.AnyAsync(x => x.Id == recentNotification.Id, cancellationToken));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Notifications.Where(x => x.RecipientUserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}