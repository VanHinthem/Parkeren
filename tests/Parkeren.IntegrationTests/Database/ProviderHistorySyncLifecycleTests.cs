using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistorySyncLifecycleTests(PostgreSqlFixture fixture)
{
    private sealed class Reader(Func<int, CancellationToken, Task<ProviderActionHistoryPage>> read)
        : IProviderActionHistoryReader
    {
        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string productId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
            read(pageNumber, cancellationToken);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task Full_run_persists_terminal_outcome(string outcome)
    {
        var token = TestContext.Current.CancellationToken;
        var product = $"lifecycle-{Guid.NewGuid():N}";
        var reader = new Reader((number, ct) => outcome switch
        {
            "failure" => throw new InvalidOperationException("History reader failed."),
            "cancel" => throw new OperationCanceledException(ct),
            _ => Task.FromResult(new ProviderActionHistoryPage([], number, 10, 0))
        });

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var service = CreateService(db, reader);
                if (outcome == "success")
                    await service.ImportAsync(product, 10, token);
                else if (outcome == "failure")
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        service.ImportAsync(product, 10, token));
                else
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        service.ImportAsync(product, 10, token));
            }

            await using (var check = fixture.CreateDbContext())
            {
                var run = await check.ProviderHistorySyncRuns.AsNoTracking()
                    .SingleAsync(x => x.ProviderProductId == product, token);
                var expected = outcome switch
                {
                    "success" => ProviderHistorySyncRunStatus.Succeeded,
                    "failure" => ProviderHistorySyncRunStatus.Failed,
                    _ => ProviderHistorySyncRunStatus.Cancelled
                };
                Assert.Equal(expected, run.Status);
                Assert.NotNull(run.FinishedAt);
                Assert.Equal(outcome == "failure" ? "History reader failed." : null, run.Error);
                Assert.Equal(0, run.ReadCount);
                var checkpoint = await check.ProviderHistorySyncStates.AsNoTracking()
                    .SingleAsync(x => x.ProviderProductId == product, token);
                Assert.Equal(0, checkpoint.NextPageNumber);
                if (outcome == "success")
                    Assert.NotNull(checkpoint.LastSuccessfulSyncAt);
                else
                    Assert.Null(checkpoint.LastSuccessfulSyncAt);
                if (outcome == "failure")
                    Assert.Equal("History reader failed.", checkpoint.LastError);
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    private static ProviderHistoryCheckpointedImportService CreateService(
        ParkerenDbContext db, IProviderActionHistoryReader reader)
    {
        var checkpoint = new ProviderHistorySyncStateStore(db);
        var pages = new ProviderHistoryTransactionalPageImporter(
            db, new ProviderHistoryPageImporter(
                new ProviderHistoryExistingActionStore(db),
                new ProviderHistoryNewActionStore(db)), checkpoint,
            new ProviderHistorySyncRunStore(db));
        return new ProviderHistoryCheckpointedImportService(
            reader, pages, checkpoint, TimeProvider.System, db,
            new ProviderHistorySyncRunStore(db));
    }
}
