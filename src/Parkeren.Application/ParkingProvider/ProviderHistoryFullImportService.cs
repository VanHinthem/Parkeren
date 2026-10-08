namespace Parkeren.Application.ParkingProvider;

/// <summary>
/// Walks the reader's existing paginated history contract without changing
/// provider pagination, matching or local persistence.
/// </summary>
public sealed class ProviderHistoryFullImportService(
    IProviderActionHistoryReader reader,
    ProviderHistoryPageImporter importer,
    TimeProvider timeProvider)
{
    public async Task<ProviderHistoryImportSummary> ImportAsync(
        string providerProductId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var inserted = 0;
        var refreshed = 0;
        var skipped = 0;
        var existing = 0;
        var pageNumber = 0;
        var observedAt = timeProvider.GetUtcNow();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await reader.GetActionHistoryPageAsync(
                providerProductId, pageNumber, pageSize, cancellationToken);
            if (page.PageNumber != pageNumber || page.PageSize != pageSize)
                throw new InvalidOperationException("Provider history reader returned an unexpected page.");
            if (page.Records.Count > pageSize)
                throw new InvalidOperationException("Provider history reader returned too many records.");

            var result = await importer.ImportPageAsync(
                providerProductId, page, observedAt, cancellationToken);
            inserted += result.Inserted;
            refreshed += result.Refreshed;
            skipped += result.SkippedManaged;
            existing += result.AlreadyExists;

            if (!page.HasMore)
                return new ProviderHistoryImportSummary(inserted, refreshed, skipped, existing);

            if (pageNumber == int.MaxValue)
                throw new InvalidOperationException("Provider history page number overflow.");
            pageNumber++;
        }
    }
}
