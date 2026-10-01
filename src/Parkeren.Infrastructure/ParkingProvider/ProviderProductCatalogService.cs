using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

internal sealed class ProviderProductCatalogService(
    ParkerenDbContext dbContext,
    IParkingProvider provider) : IProviderProductCatalogService
{
    private const long CatalogLockKey = 0x50524F44; // PROD

    public async Task<IReadOnlyList<ProviderProductSummary>> GetProductsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.ParkingProviderProducts.AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.IsAvailable)
            .ThenBy(x => x.Name)
            .Select(x => ToSummary(x))
            .ToListAsync(cancellationToken);

    public async Task<ProviderProductSyncResult> SynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        var remoteProducts = await provider.GetProductsAsync(cancellationToken);
        var duplicateIds = remoteProducts
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
            throw new InvalidOperationException("Parking provider returned duplicate product ids.");

        var seenAt = DateTimeOffset.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({CatalogLockKey})",
            cancellationToken);

        var existing = await dbContext.ParkingProviderProducts
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var hadProductsBeforeSync = existing.Count > 0;
        var byProviderId = existing.ToDictionary(x => x.ProviderProductId, StringComparer.Ordinal);

        foreach (var product in existing)
            product.MarkUnavailable();

        foreach (var remote in remoteProducts)
        {
            if (string.IsNullOrWhiteSpace(remote.Id) ||
                string.IsNullOrWhiteSpace(remote.Name) ||
                string.IsNullOrWhiteSpace(remote.Location))
                throw new InvalidOperationException("Parking provider returned an incomplete product.");

            if (byProviderId.TryGetValue(remote.Id, out var local))
            {
                local.Refresh(
                    remote.Name,
                    remote.CategoryId,
                    remote.CategoryName,
                    remote.Location,
                    seenAt);
                continue;
            }

            var created = new ParkingProviderProduct(
                Guid.NewGuid(),
                remote.Id,
                remote.Name,
                remote.CategoryId,
                remote.CategoryName,
                remote.Location,
                seenAt);
            dbContext.ParkingProviderProducts.Add(created);
            existing.Add(created);
            byProviderId.Add(remote.Id, created);
        }

        var autoSelected = false;
        if (!hadProductsBeforeSync && remoteProducts.Count == 1)
        {
            var sole = byProviderId[remoteProducts[0].Id];
            sole.SetDefault(true);
            await AssignUnboundConfigurationAsync(sole.Id, cancellationToken);
            autoSelected = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProviderProductSyncResult(
            existing
                .OrderByDescending(x => x.IsDefault)
                .ThenByDescending(x => x.IsAvailable)
                .ThenBy(x => x.Name)
                .Select(ToSummary)
                .ToArray(),
            autoSelected);
    }

    public async Task<ProviderProductSummary?> GetDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.ParkingProviderProducts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsDefault, cancellationToken);
        return product is null ? null : ToSummary(product);
    }

    public async Task<ProviderProductSummary> ResolveDefaultForStartAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await dbContext.ParkingProviderProducts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsDefault, cancellationToken);

        if (current is not null)
        {
            if (!current.IsAvailable)
                throw new InvalidOperationException("The configured default parking product is no longer available at the provider.");

            return ToSummary(current);
        }

        var hasLocalProducts = await dbContext.ParkingProviderProducts.AsNoTracking()
            .AnyAsync(cancellationToken);

        if (!hasLocalProducts)
        {
            var sync = await SynchronizeAsync(cancellationToken);
            var syncedDefault = sync.Products.SingleOrDefault(x => x.IsDefault);
            if (syncedDefault is not null && syncedDefault.IsAvailable)
                return syncedDefault;
        }

        throw new InvalidOperationException("No default parking product is configured.");
    }

    public async Task<bool> SetDefaultAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
            return false;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({CatalogLockKey})",
            cancellationToken);

        var products = await dbContext.ParkingProviderProducts
            .ToListAsync(cancellationToken);
        var selected = products.SingleOrDefault(x => x.Id == productId);
        if (selected is null || !selected.IsAvailable)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var hadDefault = products.Any(x => x.IsDefault);
        foreach (var product in products)
            product.SetDefault(product.Id == selected.Id);

        if (!hadDefault)
            await AssignUnboundConfigurationAsync(selected.Id, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task AssignUnboundConfigurationAsync(
        Guid providerProductId,
        CancellationToken cancellationToken)
    {
        var unboundRuleSets = await dbContext.ParkingRuleSets
            .Where(x => x.ProviderProductId == null)
            .ToListAsync(cancellationToken);
        foreach (var ruleSet in unboundRuleSets)
            ruleSet.AssignProviderProduct(providerProductId);

        var unboundTariffs = await dbContext.ParkingTariffs
            .Where(x => x.ProviderProductId == null)
            .ToListAsync(cancellationToken);
        foreach (var tariff in unboundTariffs)
            tariff.AssignProviderProduct(providerProductId);

        var unboundBudgets = await dbContext.ParkingBudgetPeriods
            .Where(x => x.ProviderProductId == null)
            .ToListAsync(cancellationToken);
        foreach (var budget in unboundBudgets)
            budget.AssignProviderProduct(providerProductId);
    }

    private static ProviderProductSummary ToSummary(ParkingProviderProduct product) =>
        new(
            product.Id,
            product.ProviderProductId,
            product.Name,
            product.CategoryId,
            product.CategoryName,
            product.Location,
            product.IsAvailable,
            product.IsDefault,
            product.FirstSeenAt,
            product.LastSeenAt);
}
