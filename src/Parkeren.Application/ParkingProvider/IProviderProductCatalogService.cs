namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderProductSummary(
    Guid Id,
    string ProviderProductId,
    string Name,
    string? CategoryId,
    string? CategoryName,
    string Location,
    bool IsAvailable,
    bool IsDefault,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record ProviderProductSyncResult(
    IReadOnlyList<ProviderProductSummary> Products,
    bool DefaultAutoSelected);

public interface IProviderProductCatalogService
{
    Task<IReadOnlyList<ProviderProductSummary>> GetProductsAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderProductSyncResult> SynchronizeAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderProductSummary?> GetDefaultAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderProductSummary> ResolveDefaultForStartAsync(
        CancellationToken cancellationToken = default);

    Task<bool> SetDefaultAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
