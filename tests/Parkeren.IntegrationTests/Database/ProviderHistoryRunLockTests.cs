using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryRunLockTests(PostgreSqlFixture fixture)
{
    private sealed class BlockingReader : IProviderActionHistoryReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string productId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new ProviderActionHistoryPage([], pageNumber, pageSize, 0);
        }
    }

    [Fact]
    public async Task Second_run_for_same_product_is_rejected()
    {
        var token = TestContext.Current.CancellationToken;
        var product = $"run-lock-{Guid.NewGuid():N}";
        var reader = new BlockingReader();

        try
        {
            await using var firstDb = fixture.CreateDbContext();
            await using var secondDb = fixture.CreateDbContext();
            var first = CreateService(firstDb, reader);
            var second = CreateService(secondDb, reader);
            var running = first.ImportAsync(product, 10, token);
            await reader.Started.Task.WaitAsync(token);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                second.ImportAsync(product, 10, token));

            reader.Release.TrySetResult();
            await running;
        }
        finally
        {
            reader.Release.TrySetResult();
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Background_workers_cannot_claim_the_same_reserved_run_simultaneously()
    {
        var ct = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid().ToString("D");
        const string claimSql =
            "SELECT pg_try_advisory_lock(hashtextextended({0}, 2)) AS \"Value\"";
        const string releaseSql =
            "SELECT pg_advisory_unlock(hashtextextended({0}, 2)) AS \"Value\"";

        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        await first.Database.OpenConnectionAsync(ct);
        await second.Database.OpenConnectionAsync(ct);

        var firstAcquired = await first.Database.SqlQueryRaw<bool>(claimSql, runId).SingleAsync(ct);
        Assert.True(firstAcquired);
        try
        {
            Assert.False(await second.Database.SqlQueryRaw<bool>(claimSql, runId).SingleAsync(ct));
        }
        finally
        {
            Assert.True(await first.Database.SqlQueryRaw<bool>(releaseSql, runId)
                .SingleAsync(CancellationToken.None));
        }

        Assert.True(await second.Database.SqlQueryRaw<bool>(claimSql, runId).SingleAsync(ct));
        Assert.True(await second.Database.SqlQueryRaw<bool>(releaseSql, runId)
            .SingleAsync(CancellationToken.None));
    }

    private static ProviderHistoryCheckpointedImportService CreateService(
        Parkeren.Infrastructure.Persistence.ParkerenDbContext db, IProviderActionHistoryReader reader)
    {
        var checkpoints = new ProviderHistorySyncStateStore(db);
        var pages = new ProviderHistoryTransactionalPageImporter(
            db, new ProviderHistoryPageImporter(
                new ProviderHistoryExistingActionStore(db),
                new ProviderHistoryNewActionStore(db)), checkpoints,
            new ProviderHistorySyncRunStore(db));
        return new ProviderHistoryCheckpointedImportService(
            reader, pages, checkpoints, TimeProvider.System, db,
            new ProviderHistorySyncRunStore(db));
    }
}
