namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderHistoryCheckpoint(
    int NextPageNumber,
    int PageSize,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError);

public interface IProviderHistorySyncStateStore
{
    Task<ProviderHistoryCheckpoint> GetOrCreateAsync(
        string providerProductId, int pageSize, CancellationToken cancellationToken = default);

    Task RecordPageCompletedAsync(
        string providerProductId, int pageNumber, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task RestartTraversalAsync(
        string providerProductId, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task RecordSyncCompletedAsync(
        string providerProductId, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        string providerProductId, DateTimeOffset observedAt, string message,
        CancellationToken cancellationToken = default);
}
