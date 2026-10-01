using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminSystemDiagnosticsTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Diagnostics_report_scheduler_and_push_queue_state_without_exposing_secrets()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.Parse("2026-10-01T20:00:00Z");
        await ClearAsync(ct);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"diag-{suffix}", $"DIAG-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"DG{suffix[..4]}", $"DG{suffix[..4]}", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddHours(-1),
            now.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));

        var overdue = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.StopVisit, now.AddMinutes(-10));
        var claimed = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(10));
        claimed.Claim("diagnostics-test", now.AddMinutes(-5));
        var completed = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.LongVisitWarning, now.AddMinutes(-20));
        completed.Claim("diagnostics-test", now.AddMinutes(-19));
        completed.Complete(now.AddMinutes(-18));

        var notification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddMinutes(-15));
        var pendingDelivery = new PushDelivery(Guid.NewGuid(), notification.Id, now.AddMinutes(-15));
        pendingDelivery.MarkAttempt(now.AddMinutes(-4));

        var failedNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStopped, now.AddMinutes(-12));
        var failedDelivery = new PushDelivery(Guid.NewGuid(), failedNotification.Id, now.AddMinutes(-12));
        failedDelivery.MarkAttempt(now.AddMinutes(-3));
        failedDelivery.MarkFailed();

        var deliveredNotification = new Notification(
            Guid.NewGuid(), user.Id, NotificationType.VisitStarted, now.AddMinutes(-10));
        var deliveredDelivery = new PushDelivery(Guid.NewGuid(), deliveredNotification.Id, now.AddMinutes(-10));
        deliveredDelivery.MarkAttempt(now.AddMinutes(-2));
        deliveredDelivery.MarkDelivered(now.AddMinutes(-1));

        await using (var context = fixture.CreateDbContext())
        {
            context.Users.Add(user);
            context.Vehicles.Add(vehicle);
            context.Visits.Add(visit);
            context.VisitSchedulerWork.AddRange(overdue, claimed, completed);
            context.Notifications.AddRange(notification, failedNotification, deliveredNotification);
            context.PushDeliveries.AddRange(pendingDelivery, failedDelivery, deliveredDelivery);
            await context.SaveChangesAsync(ct);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
            ["ParkingProvider:Type"] = "TwoParkMock",
            ["ParkingProvider:Username"] = "provider-secret",
            ["ParkingProvider:Password"] = "provider-password",
            ["WebPush:Subject"] = "mailto:test@example.invalid",
            ["WebPush:PublicKey"] = "public-key",
            ["WebPush:PrivateKey"] = "private-secret"
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var diagnostics = scope.ServiceProvider.GetRequiredService<IAdminSystemDiagnosticsService>();

        var result = await diagnostics.GetAsync(ct);

        Assert.True(result.Database.Healthy);
        Assert.True(result.Provider.Configured);
        Assert.Equal("TwoParkMock", result.Provider.Status);
        Assert.True(result.WebPush.Configured);

        Assert.Equal(1, result.Scheduler.PendingCount);
        Assert.Equal(1, result.Scheduler.ClaimedCount);
        Assert.Equal(1, result.Scheduler.OverdueCount);
        Assert.Equal(now.AddMinutes(-10), result.Scheduler.OldestPendingDueAt);
        Assert.Equal(now.AddMinutes(-5), result.Scheduler.OldestClaimedAt);
        Assert.Equal(now.AddMinutes(-18), result.Scheduler.LastCompletedAt);

        Assert.Equal(1, result.PushDeliveries.PendingCount);
        Assert.Equal(1, result.PushDeliveries.FailedCount);
        Assert.Equal(now.AddMinutes(-15), result.PushDeliveries.OldestPendingCreatedAt);
        Assert.Equal(now.AddMinutes(-2), result.PushDeliveries.LastAttemptAt);
        Assert.Equal(now.AddMinutes(-1), result.PushDeliveries.LastDeliveredAt);

        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("provider-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.ConnectionString, json, StringComparison.Ordinal);
    }

    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.PushDeliveries.ExecuteDeleteAsync(cancellationToken);
        await context.Notifications.ExecuteDeleteAsync(cancellationToken);
        await context.VisitSchedulerWork.ExecuteDeleteAsync(cancellationToken);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderOperations.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderParkingActions.ExecuteDeleteAsync(cancellationToken);
        await context.Visits.ExecuteDeleteAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
