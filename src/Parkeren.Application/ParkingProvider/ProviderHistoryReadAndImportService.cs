namespace Parkeren.Application.ParkingProvider;

/// <summary>
/// Reads one provider page through the existing reader and imports its records.
/// Intentionally does not implement or alter pagination, scheduling or checkpoints.
/// </summary>
public sealed class ProviderHistoryReadAndImportService(
    IProviderActionHistoryReader reader,
    ProviderHistoryPageImporter importer,
    TimeProvider timeProvider)
{
    public async Task<ProviderHistoryImportSummary> ImportPageAsync(
        string providerProductId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var page = await reader.GetActionHistoryPageAsync(
            providerProductId, pageNumber, pageSize, cancellationToken);
        return await importer.ImportPageAsync(
            providerProductId, page, timeProvider.GetUtcNow(), cancellationToken);
    }
}
