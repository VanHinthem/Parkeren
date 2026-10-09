namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderHistoryImportSummary(int Inserted, int Refreshed, int SkippedManaged, int AlreadyExists);

/// <summary>
/// Imports one already-fetched history page. Paging and checkpoints are owned by the caller.
/// </summary>
public sealed class ProviderHistoryPageImporter(
    IProviderHistoryExistingActionStore existingStore,
    IProviderHistoryNewActionStore newActionStore)
{
    public async Task<ProviderHistoryImportSummary> ImportPageAsync(
        string providerProductId,
        ProviderActionHistoryPage page,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));

        // Validate the entire page before applying any changes.
        var records = ProviderHistoryImportPlan.Prepare(page);
        var inserted = 0;
        var refreshed = 0;
        var managed = 0;
        var alreadyExists = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await existingStore.ApplyIfExistingAsync(
                providerProductId, record, observedAt, cancellationToken);
            switch (result)
            {
                case ProviderHistoryExistingActionResult.RefreshedManaged:
                case ProviderHistoryExistingActionResult.RefreshedImported:
                    refreshed++;
                    break;
                case ProviderHistoryExistingActionResult.SkippedManaged:
                    managed++;
                    break;
                case ProviderHistoryExistingActionResult.NotFound:
                    var insertion = await newActionStore.InsertIfMissingAsync(
                        providerProductId, record, observedAt, cancellationToken);
                    if (insertion == ProviderHistoryNewActionResult.Inserted)
                        inserted++;
                    else
                        alreadyExists++;
                    break;
                default:
                    throw new InvalidOperationException("Unexpected existing provider action result.");
            }
        }

        return new ProviderHistoryImportSummary(inserted, refreshed, managed, alreadyExists);
    }
}
