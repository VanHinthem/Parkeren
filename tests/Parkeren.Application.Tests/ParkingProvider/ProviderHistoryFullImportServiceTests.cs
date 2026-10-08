using Parkeren.Application.ParkingProvider;
using Xunit;

namespace Parkeren.Application.Tests.ParkingProvider;

public sealed class ProviderHistoryFullImportServiceTests
{
    private sealed class HistoryReader : IProviderActionHistoryReader
    {
        public List<int> Pages { get; } = [];

        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string productId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            Pages.Add(pageNumber);
            var start = DateTimeOffset.UnixEpoch;
            var record = new ProviderActionHistoryRecord(
                $"action-{pageNumber}", "COMPLETED", start, start.AddMinutes(5),
                0.10m, "EUR", "AB12CD", "OSS Zone J");
            return Task.FromResult(new ProviderActionHistoryPage([record], pageNumber, pageSize, 3));
        }
    }

    private sealed class ExistingStore : IProviderHistoryExistingActionStore
    {
        public Task<ProviderHistoryExistingActionResult> ApplyIfExistingAsync(
            string productId, ProviderActionHistoryRecord record,
            DateTimeOffset observedAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderHistoryExistingActionResult.NotFound);
    }

    private sealed class NewStore : IProviderHistoryNewActionStore
    {
        public List<string> Inserted { get; } = [];

        public Task<ProviderHistoryNewActionResult> InsertIfMissingAsync(
            string productId, ProviderActionHistoryRecord record,
            DateTimeOffset observedAt, CancellationToken cancellationToken = default)
        {
            Inserted.Add(record.ProviderActionId);
            return Task.FromResult(ProviderHistoryNewActionResult.Inserted);
        }
    }

    [Fact]
    public async Task Imports_all_pages_using_existing_has_more_contract()
    {
        var reader = new HistoryReader();
        var store = new NewStore();
        var service = new ProviderHistoryFullImportService(reader,
            new ProviderHistoryPageImporter(new ExistingStore(), store), TimeProvider.System);

        var result = await service.ImportAsync("product-1", 1, TestContext.Current.CancellationToken);

        Assert.Equal([0, 1, 2], reader.Pages);
        Assert.Equal(["action-0", "action-1", "action-2"], store.Inserted);
        Assert.Equal(new ProviderHistoryImportSummary(3, 0, 0, 0), result);
    }

    [Fact]
    public async Task Invalid_page_size_is_rejected_before_reading()
    {
        var reader = new HistoryReader();
        var service = new ProviderHistoryFullImportService(reader,
            new ProviderHistoryPageImporter(new ExistingStore(), new NewStore()), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ImportAsync("product-1", 0, TestContext.Current.CancellationToken));
        Assert.Empty(reader.Pages);
    }
}
