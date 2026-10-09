using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistorySyncRunStarterTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Concurrent_requests_reserve_only_one_running_sync_for_the_same_product()
    {
        var ct = TestContext.Current.CancellationToken;
        var productId = $"sync-{Guid.NewGuid():N}";
        try
        {
            async Task<ProviderHistorySyncRun?> ReserveAsync()
            {
                await using var db = fixture.CreateDbContext();
                return await new ProviderHistorySyncRunStarter(db, TimeProvider.System)
                    .TryStartAsync(productId, ProviderHistorySyncRunMode.Manual, ct);
            }

            var results = await Task.WhenAll(ReserveAsync(), ReserveAsync());
            Assert.Single(results, x => x is not null);

            await using var verify = fixture.CreateDbContext();
            var runs = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.ProviderProductId == productId).ToListAsync(ct);
            var run = Assert.Single(runs);
            Assert.Equal(ProviderHistorySyncRunStatus.Running, run.Status);
            Assert.Equal(ProviderHistorySyncRunMode.Manual, run.Mode);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == productId)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Different_products_can_reserve_independent_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefix = Guid.NewGuid().ToString("N");
        var firstProduct = $"sync-a-{prefix}";
        var secondProduct = $"sync-b-{prefix}";
        try
        {
            async Task<ProviderHistorySyncRun?> ReserveAsync(string productId)
            {
                await using var db = fixture.CreateDbContext();
                return await new ProviderHistorySyncRunStarter(db, TimeProvider.System)
                    .TryStartAsync(productId, ProviderHistorySyncRunMode.Bootstrap, ct);
            }

            var runs = await Task.WhenAll(ReserveAsync(firstProduct), ReserveAsync(secondProduct));
            Assert.All(runs, x => Assert.NotNull(x));
            Assert.NotEqual(runs[0]!.Id, runs[1]!.Id);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x =>
                x.ProviderProductId == firstProduct || x.ProviderProductId == secondProduct)
                .ExecuteDeleteAsync(ct);
        }
    }
}
