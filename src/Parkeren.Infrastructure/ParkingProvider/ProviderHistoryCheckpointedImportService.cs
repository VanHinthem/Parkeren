using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Resumes an import from the durable page checkpoint. The provider reader's
/// existing paging implementation is not modified.
/// </summary>
public sealed class ProviderHistoryCheckpointedImportService(
    IProviderActionHistoryReader reader,
    ProviderHistoryTransactionalPageImporter pages,
    IProviderHistorySyncStateStore checkpoints,
    TimeProvider timeProvider)
{
    public async Task<ProviderHistoryImportSummary> ImportAsync(
        string providerProductId, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var progress = await checkpoints.GetOrCreateAsync(
            providerProductId, pageSize, cancellationToken);
        var pageNumber = progress.NextPageNumber;
        var effectivePageSize = progress.PageSize;
        var observedAt = timeProvider.GetUtcNow();
        var inserted = 0;
        var refreshed = 0;
        var skipped = 0;
        var existing = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await reader.GetActionHistoryPageAsync(
                    providerProductId, pageNumber, effectivePageSize, cancellationToken);

                if (page.PageNumber != pageNumber || page.PageSize != effectivePageSize)
                    throw new InvalidOperationException("Provider history reader returned an unexpected page.");
                if (page.Records.Count > effectivePageSize)
                    throw new InvalidOperationException("Provider history reader returned too many records.");
                if (page.HasMore && page.Records.Count == 0)
                    throw new InvalidOperationException("Provider history reader returned an empty nonterminal page.");

                var result = await pages.ImportPageAsync(
                    providerProductId, page, observedAt, cancellationToken);
                inserted += result.Inserted;
                refreshed += result.Refreshed;
                skipped += result.SkippedManaged;
                existing += result.AlreadyExists;

                if (!page.HasMore)
                {
                    await checkpoints.RecordSyncCompletedAsync(
                        providerProductId, timeProvider.GetUtcNow(), cancellationToken);
                    return new ProviderHistoryImportSummary(inserted, refreshed, skipped, existing);
                }

                if (pageNumber == int.MaxValue)
                    throw new InvalidOperationException("Provider history page number overflow.");
                pageNumber++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await checkpoints.RecordFailureAsync(
                providerProductId, timeProvider.GetUtcNow(), ex.Message, cancellationToken);
            throw;
        }
    }
}
