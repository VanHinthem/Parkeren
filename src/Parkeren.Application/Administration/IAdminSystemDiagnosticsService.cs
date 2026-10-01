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
    AdminQueueDiagnostics Scheduler,
    AdminQueueDiagnostics PushDeliveries);

public sealed record AdminDatabaseDiagnostics(
    bool Healthy,
    string Status);

public sealed record AdminConfigurationDiagnostics(
    bool Configured,
    string Status);

public sealed record AdminQueueDiagnostics(
    int Pending,
    int Claimed,
    int Completed,
    int Cancelled,
    int Failed,
    DateTimeOffset? OldestPendingAt,
    DateTimeOffset? LastCompletedAt);
