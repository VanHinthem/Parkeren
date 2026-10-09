namespace Parkeren.Application.ParkingProvider;

public enum ProviderHistoryNewActionResult
{
    Inserted,
    AlreadyExists
}

/// <summary>
/// Inserts previously unknown completed provider actions. Existing IDs are left to
/// IProviderHistoryExistingActionStore for explicit reconciliation.
/// </summary>
public interface IProviderHistoryNewActionStore
{
    Task<ProviderHistoryNewActionResult> InsertIfMissingAsync(
        string providerProductId,
        ProviderActionHistoryRecord record,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);
}
