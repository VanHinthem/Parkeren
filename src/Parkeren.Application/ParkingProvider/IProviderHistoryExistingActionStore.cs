namespace Parkeren.Application.ParkingProvider;

public enum ProviderHistoryExistingActionResult
{
    NotFound,
    RefreshedImported,
    RefreshedManaged,
    SkippedManaged
}

/// <summary>
/// Matches existing actions solely by the stable provider action ID.
/// A NotFound result requires a separate vehicle-aware import path.
/// </summary>
public interface IProviderHistoryExistingActionStore
{
    Task<ProviderHistoryExistingActionResult> ApplyIfExistingAsync(
        string providerProductId,
        ProviderActionHistoryRecord record,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);
}
