using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

internal sealed class ProviderHistoryExistingActionStore(ParkerenDbContext dbContext)
    : IProviderHistoryExistingActionStore
{
    public async Task<ProviderHistoryExistingActionResult> ApplyIfExistingAsync(
        string providerProductId,
        ProviderActionHistoryRecord record,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProviderActionId))
            throw new ArgumentException("Provider action id is required.", nameof(record));

        var providerActionId = record.ProviderActionId.Trim();
        var existing = await dbContext.ProviderParkingActions
            .SingleOrDefaultAsync(x => x.ProviderActionId == providerActionId, cancellationToken);
        if (existing is null)
            return ProviderHistoryExistingActionResult.NotFound;

        // Existing index is globally unique. Never reinterpret the same provider action
        // ID as belonging to a different product, even if it is returned by that product.
        if (!string.Equals(existing.ProviderProductId, providerProductId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Provider action {providerActionId} belongs to a different product.");

        if (existing.Origin == ProviderActionOrigin.Managed)
            return ProviderHistoryExistingActionResult.SkippedManaged;

        existing.RefreshImportedHistory(
            record.ActualStartAt,
            record.ActualEndAt,
            record.ProviderCostAmount,
            record.Status,
            observedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProviderHistoryExistingActionResult.RefreshedImported;
    }
}
