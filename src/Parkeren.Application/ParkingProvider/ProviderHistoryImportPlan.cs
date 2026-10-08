namespace Parkeren.Application.ParkingProvider;

/// <summary>
/// Validates one history page before persistence. Action IDs are the only matching identity;
/// conflicting duplicate payloads are rejected rather than guessed into another action.
/// </summary>
public static class ProviderHistoryImportPlan
{
    public static IReadOnlyList<ProviderActionHistoryRecord> Prepare(
        ProviderActionHistoryPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Records is null)
            throw new ArgumentException("History page records are required.", nameof(page));

        var result = new List<ProviderActionHistoryRecord>();
        var seen = new Dictionary<string, ProviderActionHistoryRecord>(StringComparer.Ordinal);

        foreach (var record in page.Records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.ProviderActionId))
                throw new InvalidOperationException("History includes a record without a provider action id.");
            if (record.ActualEndAt < record.ActualStartAt)
                throw new InvalidOperationException($"History action {record.ProviderActionId} has an invalid interval.");
            if (record.ProviderCostAmount is < 0m)
                throw new InvalidOperationException($"History action {record.ProviderActionId} has negative cost.");

            var id = record.ProviderActionId.Trim();
            var normalized = record with { ProviderActionId = id };
            if (seen.TryGetValue(id, out var previous))
            {
                if (previous != normalized)
                    throw new InvalidOperationException($"Conflicting provider history records for action {id}.");
                continue;
            }

            seen.Add(id, normalized);
            result.Add(normalized);
        }

        return result;
    }
}
