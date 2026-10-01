using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.Administration;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Administration;

internal sealed class AdminSystemDiagnosticsService(
    ParkerenDbContext dbContext,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAdminSystemDiagnosticsService
{
    public async Task<AdminSystemDiagnosticsSummary> GetAsync(CancellationToken cancellationToken)
    {
        var observedAt = timeProvider.GetUtcNow();
        var databaseHealthy = await dbContext.Database.CanConnectAsync(cancellationToken);

        var scheduler = await dbContext.VisitSchedulerWork
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(x => x.Status == VisitSchedulerWorkStatus.Pending),
                Claimed = group.Count(x => x.Status == VisitSchedulerWorkStatus.Claimed),
                Completed = group.Count(x => x.Status == VisitSchedulerWorkStatus.Completed),
                Cancelled = group.Count(x => x.Status == VisitSchedulerWorkStatus.Cancelled),
                OldestPendingAt = group
                    .Where(x => x.Status == VisitSchedulerWorkStatus.Pending)
                    .Select(x => (DateTimeOffset?)x.DueAt)
                    .Min(),
                LastCompletedAt = group
                    .Where(x => x.Status == VisitSchedulerWorkStatus.Completed)
                    .Select(x => x.CompletedAt)
                    .Max()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var push = await dbContext.PushDeliveries
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(x => x.Status == PushDeliveryStatus.Pending),
                Delivered = group.Count(x => x.Status == PushDeliveryStatus.Delivered),
                Failed = group.Count(x => x.Status == PushDeliveryStatus.Failed),
                OldestPendingAt = group
                    .Where(x => x.Status == PushDeliveryStatus.Pending)
                    .Select(x => (DateTimeOffset?)x.CreatedAt)
                    .Min(),
                LastDeliveredAt = group
                    .Where(x => x.Status == PushDeliveryStatus.Delivered)
                    .Select(x => x.DeliveredAt)
                    .Max()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var providerType = configuration["ParkingProvider:Type"];
        var providerConfigured = providerType is "TwoPark" or "TwoParkMock";
        var webPushConfigured =
            !string.IsNullOrWhiteSpace(configuration["WebPush:Subject"]) &&
            !string.IsNullOrWhiteSpace(configuration["WebPush:PublicKey"]) &&
            !string.IsNullOrWhiteSpace(configuration["WebPush:PrivateKey"]);

        return new AdminSystemDiagnosticsSummary(
            observedAt,
            new AdminDatabaseDiagnostics(databaseHealthy, databaseHealthy ? "Connected" : "Unavailable"),
            new AdminConfigurationDiagnostics(
                providerConfigured,
                providerConfigured ? providerType! : "NotConfigured"),
            new AdminConfigurationDiagnostics(
                webPushConfigured,
                webPushConfigured ? "Configured" : "NotConfigured"),
            new AdminQueueDiagnostics(
                scheduler?.Pending ?? 0,
                scheduler?.Claimed ?? 0,
                scheduler?.Completed ?? 0,
                scheduler?.Cancelled ?? 0,
                Failed: 0,
                scheduler?.OldestPendingAt,
                scheduler?.LastCompletedAt),
            new AdminQueueDiagnostics(
                push?.Pending ?? 0,
                Claimed: 0,
                push?.Delivered ?? 0,
                Cancelled: 0,
                push?.Failed ?? 0,
                push?.OldestPendingAt,
                push?.LastDeliveredAt));
    }
}
