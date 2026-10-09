using Parkeren.Application.ParkingProvider;
using Xunit;

namespace Parkeren.Application.Tests.ParkingProvider;

public sealed class ProviderHistoryReadAndImportServiceTests
{
    private sealed class Reader : IProviderActionHistoryReader
    {
        public int Calls { get; private set; }
        public string? LastProduct { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string providerProductId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastProduct = providerProductId;
            LastPage = pageNumber;
            LastPageSize = pageSize;
            var record = new ProviderActionHistoryRecord(
                "provider-1", "COMPLETED", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(15), 0.25m,
                "EUR", "AB12CD", "OSS Zone J");
            return Task.FromResult(new ProviderActionHistoryPage([record], pageNumber, pageSize, 1));
        }
    }

    private sealed class Existing : IProviderHistoryExistingActionStore
    {
        public Task<ProviderHistoryExistingActionResult> ApplyIfExistingAsync(
            string providerProductId, ProviderActionHistoryRecord record,
            DateTimeOffset observedAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderHistoryExistingActionResult.SkippedManaged);
    }

    private sealed class NewAction : IProviderHistoryNewActionStore
    {
        public Task<ProviderHistoryNewActionResult> InsertIfMissingAsync(
            string providerProductId, ProviderActionHistoryRecord record,
            DateTimeOffset observedAt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Should not insert an already managed action.");
    }

    [Fact]
    public async Task Reads_exact_requested_page_and_routes_it_to_importer()
    {
        var reader = new Reader();
        var importer = new ProviderHistoryPageImporter(new Existing(), new NewAction());
        var service = new ProviderHistoryReadAndImportService(reader, importer, TimeProvider.System);

        var summary = await service.ImportPageAsync(
            "product-1", 2, 10, TestContext.Current.CancellationToken);

        Assert.Equal(1, reader.Calls);
        Assert.Equal("product-1", reader.LastProduct);
        Assert.Equal(2, reader.LastPage);
        Assert.Equal(10, reader.LastPageSize);
        Assert.Equal(new ProviderHistoryImportSummary(0, 0, 1, 0), summary);
    }

    [Fact]
    public async Task Invalid_paging_request_does_not_call_reader()
    {
        var reader = new Reader();
        var service = new ProviderHistoryReadAndImportService(
            reader, new ProviderHistoryPageImporter(new Existing(), new NewAction()), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ImportPageAsync("product-1", -1, 10, TestContext.Current.CancellationToken));

        Assert.Equal(0, reader.Calls);
    }
}
