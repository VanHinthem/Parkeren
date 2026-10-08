using System.Text;
using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Reserves a manual history-sync run without contacting the external provider.
/// The transaction-scoped advisory lock makes the running-run check atomic
/// across API instances for the same provider product.
/// </summary>
public sealed class ProviderHistorySyncRunStarter(
    ParkerenDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<ProviderHistorySyncRun?> TryStartAsync(
        string providerProductId,
        ProviderHistorySyncRunMode mode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product is required.", nameof(providerProductId));

        var productId = providerProductId.Trim();
        if (productId.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(providerProductId));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Namespace and product-specific key; do not use string.GetHashCode(), which is unstable.
        var key = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("provider-history-sync:" + productId));
        var lockKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(0, 8));
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        if (await dbContext.ProviderHistorySyncRuns.AsNoTracking().AnyAsync(
                x => x.ProviderProductId == productId &&
                     x.Status == ProviderHistorySyncRunStatus.Running,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var run = new ProviderHistorySyncRun(
            Guid.NewGuid(), productId, mode, timeProvider.GetUtcNow());
        dbContext.ProviderHistorySyncRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return run;
    }
}
