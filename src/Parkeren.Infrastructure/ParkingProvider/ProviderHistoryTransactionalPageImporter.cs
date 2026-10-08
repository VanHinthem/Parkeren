using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Commits a complete history page and its checkpoint together. All import stores
/// must use the same scoped ParkerenDbContext for transaction enlistment.
/// </summary>
public sealed class ProviderHistoryTransactionalPageImporter(
    ParkerenDbContext db,
    ProviderHistoryPageImporter importer,
    IProviderHistorySyncStateStore checkpoints)
{
    public async Task<ProviderHistoryImportSummary> ImportPageAsync(
        string providerProductId,
        ProviderActionHistoryPage page,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Product id is required.", nameof(providerProductId));

        // Reject malformed pages before opening a write transaction.
        ProviderHistoryImportPlan.Prepare(page);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize imports for the same product, including its first page when
        // the checkpoint row does not yet exist. The lock is released at commit/rollback.
        await db.Database.SqlQueryRaw<int>(
            "SELECT 1 AS \"Value\" FROM pg_advisory_xact_lock(hashtextextended({0}, 0))",
            providerProductId.Trim()).SingleAsync(cancellationToken);

        var checkpoint = await checkpoints.GetOrCreateAsync(
            providerProductId, page.PageSize, cancellationToken);
        if (checkpoint.NextPageNumber != page.PageNumber)
            throw new InvalidOperationException("History page does not match persisted checkpoint.");

        var result = await importer.ImportPageAsync(
            providerProductId, page, observedAt, cancellationToken);
        await checkpoints.RecordPageCompletedAsync(
            providerProductId, page.PageNumber, observedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
