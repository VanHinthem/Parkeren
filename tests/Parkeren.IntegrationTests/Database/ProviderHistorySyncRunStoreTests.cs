using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistorySyncRunStoreTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Run_progress_and_success_survive_new_database_context()
    {
        var token = TestContext.Current.CancellationToken;
        var product = $"run-{Guid.NewGuid():N}";
        var started = new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);
        Guid runId = Guid.Empty;
        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncRunStore(db);
                runId = await store.StartAsync(product, ProviderHistorySyncRunMode.Manual, started, token);
                await store.RecordPageAsync(runId, 10, 6, 2, 2, token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncRunStore(db);
                await store.CompleteAsync(runId, started.AddMinutes(2), token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var run = await db.ProviderHistorySyncRuns.AsNoTracking()
                    .SingleAsync(x => x.Id == runId, token);
                Assert.Equal(ProviderHistorySyncRunStatus.Succeeded, run.Status);
                Assert.Equal(ProviderHistorySyncRunMode.Manual, run.Mode);
                Assert.Equal(10, run.ReadCount);
                Assert.Equal(6, run.InsertedCount);
                Assert.Equal(2, run.RefreshedCount);
                Assert.Equal(2, run.SkippedCount);
                Assert.Equal(started.AddMinutes(2), run.FinishedAt);
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }
}
