namespace Parkeren.Application.Administration;

public interface IAdminSystemDiagnosticsService
{
    Task<AdminSystemDiagnosticsSummary> GetAsync(CancellationToken cancellationToken);
}

public sealed record AdminSystemDiagnosticsSummary(
    DateTimeOffset ObservedAt,
    AdminDatabaseDiagnostics Database,
    AdminConfigurationDiagnostics Provider,
    AdminConfigurationDiagnostics WebPush,
    AdminSchedulerDiagnostics Scheduler,
    AdminPushDeliveryDiagnostics PushDeliveries);

public sealed record AdminDatabaseDiagnostics(
    bool Healthy,
    string Status);

public sealed record AdminConfigurationDiagnostics(
    bool Configured,
    string Status);

public sealed record AdminSchedulerDiagnostics(
    int PendingCount,
    int ClaimedCount,
    int OverdueCount,
    DateTimeOffset? OldestPendingDueAt,
    DateTimeOffset? OldestClaimedAt,
    DateTimeOffset? LastCompletedAt);

public sealed record AdminPushDeliveryDiagnostics(
    int PendingCount,
    int FailedCount,
    DateTimeOffset? OldestPendingCreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastDeliveredAt);
