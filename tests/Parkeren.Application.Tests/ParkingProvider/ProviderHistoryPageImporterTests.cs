using Parkeren.Application.ParkingProvider;
using Xunit;

namespace Parkeren.Application.Tests.ParkingProvider;

public sealed class ProviderHistoryPageImporterTests
{
    private sealed class ExistingStore : IProviderHistoryExistingActionStore
    {
        public Dictionary<string, ProviderHistoryExistingActionResult> Results { get; } = new();
        public List<string> Calls { get; } = [];
        public Task<ProviderHistoryExistingActionResult> ApplyIfExistingAsync(
            string productId, ProviderActionHistoryRecord record, DateTimeOffset observedAt,
            CancellationToken token = default)
        {
            Calls.Add(record.ProviderActionId);
            return Task.FromResult(Results.GetValueOrDefault(
                record.ProviderActionId, ProviderHistoryExistingActionResult.NotFound));
        }
    }

    private sealed class NewStore : IProviderHistoryNewActionStore
    {
        public List<string> Calls { get; } = [];
        public Task<ProviderHistoryNewActionResult> InsertIfMissingAsync(
            string productId, ProviderActionHistoryRecord record, DateTimeOffset observedAt,
            CancellationToken token = default)
        {
            Calls.Add(record.ProviderActionId);
            return Task.FromResult(ProviderHistoryNewActionResult.Inserted);
        }
    }

    private static ProviderActionHistoryRecord Record(string id) =>
        new(id, "COMPLETED", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(20),
            0.10m, "EUR", "AB12CD", "OSS Zone J");

    [Fact]
    public async Task Page_import_routes_each_unique_action_id_once()
    {
        var existing = new ExistingStore();
        existing.Results["managed"] = ProviderHistoryExistingActionResult.SkippedManaged;
        existing.Results["imported"] = ProviderHistoryExistingActionResult.RefreshedImported;
        var fresh = new NewStore();
        var importer = new ProviderHistoryPageImporter(existing, fresh);
        var page = new ProviderActionHistoryPage(
            [Record("new"), Record("managed"), Record("imported"), Record("new")], 0, 10, 4);

        var result = await importer.ImportPageAsync("product-1", page, DateTimeOffset.UtcNow);

        Assert.Equal(new ProviderHistoryImportSummary(1, 1, 1, 0), result);
        Assert.Equal(["new", "managed", "imported"], existing.Calls);
        Assert.Equal(["new"], fresh.Calls);
    }

    [Fact]
    public async Task Invalid_page_fails_before_any_database_writes()
    {
        var existing = new ExistingStore();
        var fresh = new NewStore();
        var importer = new ProviderHistoryPageImporter(existing, fresh);
        var page = new ProviderActionHistoryPage([Record("valid"), Record("")], 0, 10, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ImportPageAsync("product-1", page, DateTimeOffset.UtcNow));

        Assert.Empty(existing.Calls);
        Assert.Empty(fresh.Calls);
    }
}
