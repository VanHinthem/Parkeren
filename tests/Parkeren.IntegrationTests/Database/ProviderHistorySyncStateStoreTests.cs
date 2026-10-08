using Microsoft.EntityFrameworkCore;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistorySyncStateStoreTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Checkpoint_and_completion_survive_new_database_contexts()
    {
        var token = TestContext.Current.CancellationToken;
        var productId = $"sync-{Guid.NewGuid():N}";
        var observed = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero);

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                var initial = await store.GetOrCreateAsync(productId, 10, token);
                Assert.Equal(0, initial.NextPageNumber);
                await store.RecordPageCompletedAsync(productId, 0, observed, token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                var resumed = await store.GetOrCreateAsync(productId, 10, token);
                Assert.Equal(1, resumed.NextPageNumber);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    store.RecordPageCompletedAsync(productId, 0, observed, token));
                await store.RecordSyncCompletedAsync(productId, observed, token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                var completed = await store.GetOrCreateAsync(productId, 10, token);
                Assert.Equal(0, completed.NextPageNumber);
                Assert.Equal(observed, completed.LastSuccessfulSyncAt);
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderHistorySyncStates.Where(x => x.ProviderProductId == productId)
                .ExecuteDeleteAsync(token);
        }
    }
}
