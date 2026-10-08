using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class ProviderHistorySyncStateStore(ParkerenDbContext db)
    : IProviderHistorySyncStateStore
{
    public async Task<ProviderHistoryCheckpoint> GetOrCreateAsync(
        string providerProductId, int pageSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Product id is required.", nameof(providerProductId));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var productId = providerProductId.Trim();
        var state = await db.ProviderHistorySyncStates.SingleOrDefaultAsync(
            x => x.ProviderProductId == productId, cancellationToken);
        if (state is null)
        {
            state = new ProviderHistorySyncState(Guid.NewGuid(), productId, pageSize);
            db.ProviderHistorySyncStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (state.NextPageNumber > 0 && state.PageSize != pageSize)
        {
            throw new InvalidOperationException("Page size cannot change during an incomplete import.");
        }

        return new ProviderHistoryCheckpoint(state.NextPageNumber, state.PageSize,
            state.LastSuccessfulSyncAt, state.LastAttemptAt, state.LastError);
    }

    public async Task RecordPageCompletedAsync(
        string providerProductId, int pageNumber, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await FindAsync(providerProductId, cancellationToken);
        state.RecordPageCompleted(pageNumber, observedAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordSyncCompletedAsync(
        string providerProductId, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await FindAsync(providerProductId, cancellationToken);
        state.RecordSyncCompleted(observedAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(
        string providerProductId, DateTimeOffset observedAt, string message,
        CancellationToken cancellationToken = default)
    {
        var state = await FindAsync(providerProductId, cancellationToken);
        state.RecordFailure(observedAt, message);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProviderHistorySyncState> FindAsync(
        string productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product id is required.", nameof(productId));
        return await db.ProviderHistorySyncStates.SingleAsync(
            x => x.ProviderProductId == productId.Trim(), cancellationToken);
    }
}
