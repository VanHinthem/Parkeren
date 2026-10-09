namespace Parkeren.Domain.ParkingProvider;

/// <summary>
/// Determines whether a product is eligible for recurring history reconciliation.
/// A product without a completed manual/bootstrap import is never scheduled.
/// </summary>
public static class ProviderHistoryPeriodicSyncPolicy
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);

    public static bool IsDue(
        DateTimeOffset? lastSuccessfulSyncAt,
        DateTimeOffset? lastAttemptAt,
        DateTimeOffset now)
    {
        if (lastSuccessfulSyncAt is null)
            return false;

        var latestAttempt = lastAttemptAt is { } attempt && attempt > lastSuccessfulSyncAt.Value
            ? attempt
            : lastSuccessfulSyncAt.Value;

        return now - latestAttempt >= MinimumInterval;
    }
}
