using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryPeriodicSyncRunSchedulerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Concurrent_schedulers_reserve_only_one_incremental_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"periodic-reserve-{Guid.NewGuid():N}";
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                var state = new ProviderHistorySyncState(Guid.NewGuid(), product, 10);
                state.RecordSyncCompleted(DateTimeOffset.UtcNow.AddHours(-1));
                seed.ProviderHistorySyncStates.Add(state);
                await seed.SaveChangesAsync(ct);
            }

            async Task<int> ScheduleAsync()
            {
                await using var db = fixture.CreateDbContext();
                var scheduler = new ProviderHistoryPeriodicSyncRunScheduler(
                    db, new ProviderHistorySyncRunStarter(db, TimeProvider.System),
                    TimeProvider.System);
                return await scheduler.ReserveDueRunsAsync(ct);
            }

            var results = await Task.WhenAll(ScheduleAsync(), ScheduleAsync());
            Assert.Equal(1, results.Sum());

            await using var verify = fixture.CreateDbContext();
            var run = Assert.Single(await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.ProviderProductId == product).ToListAsync(ct));
            Assert.Equal(ProviderHistorySyncRunMode.Incremental, run.Mode);
            Assert.Equal(ProviderHistorySyncRunStatus.Running, run.Status);

            Assert.Equal(0, await ScheduleAsync());
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }
}
