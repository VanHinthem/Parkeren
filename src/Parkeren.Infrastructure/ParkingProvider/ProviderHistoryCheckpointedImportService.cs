using Microsoft.EntityFrameworkCore;
using Parkeren.Infrastructure.Persistence;
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
    TimeProvider timeProvider,
    ParkerenDbContext db)
{
    public async Task<ProviderHistoryImportSummary> ImportAsync(
        string providerProductId, int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        // A session-level advisory lock spans provider reads and checkpoint reset,
        // not only the individual page transactions.
        var key = providerProductId.Trim();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var acquired = false;
        try
        {
            acquired = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(hashtextextended({0}, 1)) AS \"Value\"",
                key).SingleAsync(cancellationToken);
            if (!acquired)
                throw new InvalidOperationException("A provider history sync is already running for this product.");

            return await ImportLockedAsync(key, pageSize, cancellationToken);
        }
        finally
        {
            if (acquired)
            {
                await db.Database.SqlQueryRaw<bool>(
                    "SELECT pg_advisory_unlock(hashtextextended({0}, 1)) AS \"Value\"",
                    key).SingleAsync(CancellationToken.None);
            }
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<ProviderHistoryImportSummary> ImportLockedAsync(
        string providerProductId, int pageSize, CancellationToken cancellationToken)
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
