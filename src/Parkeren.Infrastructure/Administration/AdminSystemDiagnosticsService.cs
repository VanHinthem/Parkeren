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
                PendingCount = group.Count(x => x.Status == VisitSchedulerWorkStatus.Pending),
                ClaimedCount = group.Count(x => x.Status == VisitSchedulerWorkStatus.Claimed),
                OverdueCount = group.Count(x =>
                    x.Status == VisitSchedulerWorkStatus.Pending && x.DueAt <= observedAt),
                OldestPendingDueAt = group
                    .Where(x => x.Status == VisitSchedulerWorkStatus.Pending)
                    .Select(x => (DateTimeOffset?)x.DueAt)
                    .Min(),
                OldestClaimedAt = group
                    .Where(x => x.Status == VisitSchedulerWorkStatus.Claimed)
                    .Select(x => x.ClaimedAt)
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
                PendingCount = group.Count(x => x.Status == PushDeliveryStatus.Pending),
                FailedCount = group.Count(x => x.Status == PushDeliveryStatus.Failed),
                OldestPendingCreatedAt = group
                    .Where(x => x.Status == PushDeliveryStatus.Pending)
                    .Select(x => (DateTimeOffset?)x.CreatedAt)
                    .Min(),
                LastAttemptAt = group
                    .Where(x => x.LastAttemptAt != null)
                    .Select(x => x.LastAttemptAt)
                    .Max(),
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
            new AdminSchedulerDiagnostics(
                scheduler?.PendingCount ?? 0,
                scheduler?.ClaimedCount ?? 0,
                scheduler?.OverdueCount ?? 0,
                scheduler?.OldestPendingDueAt,
                scheduler?.OldestClaimedAt,
                scheduler?.LastCompletedAt),
            new AdminPushDeliveryDiagnostics(
                push?.PendingCount ?? 0,
                push?.FailedCount ?? 0,
                push?.OldestPendingCreatedAt,
                push?.LastAttemptAt,
                push?.LastDeliveredAt));
    }
}
