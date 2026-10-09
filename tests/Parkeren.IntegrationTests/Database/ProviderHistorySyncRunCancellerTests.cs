using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistorySyncRunCancellerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Queued_run_can_be_cancelled_and_cannot_be_cancelled_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"cancel-{Guid.NewGuid():N}";
        try
        {
            Guid id;
            await using (var db = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(db, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                id = run.Id;
            }

            await using (var db = fixture.CreateDbContext())
            {
                var canceller = new ProviderHistorySyncRunCanceller(db, TimeProvider.System);
                Assert.True(await canceller.TryCancelAsync(id, ct));
                Assert.False(await canceller.TryCancelAsync(id, ct));
                Assert.Null(await canceller.TryCancelAsync(Guid.NewGuid(), ct));
            }

            await using var verify = fixture.CreateDbContext();
            var saved = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .SingleAsync(x => x.Id == id, ct);
            Assert.Equal(ProviderHistorySyncRunStatus.Cancelled, saved.Status);
            Assert.NotNull(saved.FinishedAt);
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Worker_claim_prevents_cancellation_until_released()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"claimed-{Guid.NewGuid():N}";
        const string claimSql =
            "SELECT pg_try_advisory_lock(hashtextextended({0}, 2)) AS \"Value\"";
        const string unlockSql =
            "SELECT pg_advisory_unlock(hashtextextended({0}, 2)) AS \"Value\"";
        try
        {
            Guid id;
            await using (var reserve = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(reserve, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                id = run.Id;
            }

            await using var worker = fixture.CreateDbContext();
            await worker.Database.OpenConnectionAsync(ct);
            var key = id.ToString("D");
            Assert.True(await worker.Database.SqlQueryRaw<bool>(claimSql, key).SingleAsync(ct));
            try
            {
                await using var db = fixture.CreateDbContext();
                Assert.False(await new ProviderHistorySyncRunCanceller(db, TimeProvider.System)
                    .TryCancelAsync(id, ct));
                var status = await db.ProviderHistorySyncRuns.AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Status).SingleAsync(ct);
                Assert.Equal(ProviderHistorySyncRunStatus.Running, status);
            }
            finally
            {
                Assert.True(await worker.Database.SqlQueryRaw<bool>(unlockSql, key)
                    .SingleAsync(CancellationToken.None));
            }

            await using var cancellable = fixture.CreateDbContext();
            Assert.True(await new ProviderHistorySyncRunCanceller(cancellable, TimeProvider.System)
                .TryCancelAsync(id, ct));
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }
}
