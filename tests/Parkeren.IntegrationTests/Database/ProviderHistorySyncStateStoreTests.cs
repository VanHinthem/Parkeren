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
    [Fact]
    public async Task Restart_traversal_resets_only_checkpoint_and_preserves_success_timestamp()
    {
        var token = TestContext.Current.CancellationToken;
        var product = $"restart-{Guid.NewGuid():N}";
        var initial = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var restarted = initial.AddMinutes(10);
        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                await store.GetOrCreateAsync(product, 10, token);
                await store.RecordSyncCompletedAsync(product, initial, token);
                await store.RecordPageCompletedAsync(product, 0, initial.AddMinutes(5), token);
                await store.RecordFailureAsync(product, initial.AddMinutes(6), "temporary", token);
                await store.RestartTraversalAsync(product, restarted, token);
            }
            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                var state = await store.GetOrCreateAsync(product, 10, token);
                Assert.Equal(0, state.NextPageNumber);
                Assert.Equal(10, state.PageSize);
                Assert.Equal(initial, state.LastSuccessfulSyncAt);
                Assert.Equal(restarted, state.LastAttemptAt);
                Assert.Null(state.LastError);
                await store.RecordPageCompletedAsync(product, 0, restarted, token);
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Failure_is_persisted_and_subsequent_completion_clears_error()
    {
        var token = TestContext.Current.CancellationToken;
        var productId = $"failure-sync-{Guid.NewGuid():N}";
        var attempted = new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);
        var completedAt = attempted.AddMinutes(5);

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                await store.GetOrCreateAsync(productId, 10, token);
                await store.RecordPageCompletedAsync(productId, 0, attempted, token);
                await store.RecordFailureAsync(productId, attempted, "Provider unavailable", token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistorySyncStateStore(db);
                var failed = await store.GetOrCreateAsync(productId, 10, token);
                Assert.Equal(1, failed.NextPageNumber);
                Assert.Equal("Provider unavailable", failed.LastError);
                Assert.Equal(attempted, failed.LastAttemptAt);
                Assert.Null(failed.LastSuccessfulSyncAt);
                await store.RecordSyncCompletedAsync(productId, completedAt, token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var completed = await new ProviderHistorySyncStateStore(db)
                    .GetOrCreateAsync(productId, 10, token);
                Assert.Equal(0, completed.NextPageNumber);
                Assert.Null(completed.LastError);
                Assert.Equal(completedAt, completed.LastAttemptAt);
                Assert.Equal(completedAt, completed.LastSuccessfulSyncAt);
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
