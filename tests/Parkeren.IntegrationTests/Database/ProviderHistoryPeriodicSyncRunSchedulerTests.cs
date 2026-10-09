using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryPeriodicSyncRunSchedulerTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Eligible_product_reserves_and_executes_incremental_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"periodic-execute-{Guid.NewGuid():N}";
        var pagesRead = new List<int>();
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                var state = new ProviderHistorySyncState(Guid.NewGuid(), product, 10);
                state.RecordSyncCompleted(DateTimeOffset.UtcNow.AddHours(-1));
                seed.ProviderHistorySyncStates.Add(state);
                await seed.SaveChangesAsync(ct);
            }

            Guid runId;
            await using (var reserve = fixture.CreateDbContext())
            {
                var scheduler = new ProviderHistoryPeriodicSyncRunScheduler(
                    reserve, new ProviderHistorySyncRunStarter(reserve, TimeProvider.System),
                    TimeProvider.System);
                Assert.Equal(1, await scheduler.ReserveDueRunsAsync(ct));
                runId = await reserve.ProviderHistorySyncRuns.AsNoTracking()
                    .Where(x => x.ProviderProductId == product)
                    .Select(x => x.Id).SingleAsync(ct);
            }

            await using (var execute = fixture.CreateDbContext())
            {
                var reader = new RecordingReader(pagesRead);
                var checkpoints = new ProviderHistorySyncStateStore(execute);
                var runs = new ProviderHistorySyncRunStore(execute);
                var pages = new ProviderHistoryTransactionalPageImporter(
                    execute, new ProviderHistoryPageImporter(
                        new ProviderHistoryExistingActionStore(execute),
                        new ProviderHistoryNewActionStore(execute)), checkpoints, runs);
                var importer = new ProviderHistoryCheckpointedImportService(
                    reader, pages, checkpoints, TimeProvider.System, execute, runs);
                await new ProviderHistoryReservedRunExecutor(
                    execute, importer, runs, TimeProvider.System).ExecuteAsync(runId, ct);
            }

            Assert.Equal([0], pagesRead);
            await using var verify = fixture.CreateDbContext();
            var run = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .SingleAsync(x => x.Id == runId, ct);
            Assert.Equal(ProviderHistorySyncRunMode.Incremental, run.Mode);
            Assert.Equal(ProviderHistorySyncRunStatus.Succeeded, run.Status);
            Assert.NotNull(run.FinishedAt);
            var stateAfter = await verify.ProviderHistorySyncStates.AsNoTracking()
                .SingleAsync(x => x.ProviderProductId == product, ct);
            Assert.Equal(0, stateAfter.NextPageNumber);
            Assert.True(stateAfter.LastSuccessfulSyncAt > DateTimeOffset.UtcNow.AddMinutes(-5));
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

    private sealed class RecordingReader(List<int> pagesRead) : IProviderActionHistoryReader
    {
        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string providerProductId, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            pagesRead.Add(pageNumber);
            return Task.FromResult(new ProviderActionHistoryPage([], pageNumber, pageSize, 0));
        }
    }

    [Fact]
    public async Task Failed_incremental_import_waits_before_reservation_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"periodic-retry-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                var state = new ProviderHistorySyncState(Guid.NewGuid(), product, 10);
                state.RecordSyncCompleted(now.AddHours(-1));
                state.RecordFailure(now.AddMinutes(-1), "Temporary provider failure");
                seed.ProviderHistorySyncStates.Add(state);
                await seed.SaveChangesAsync(ct);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var scheduler = new ProviderHistoryPeriodicSyncRunScheduler(
                    db, new ProviderHistorySyncRunStarter(db, TimeProvider.System),
                    TimeProvider.System);
                Assert.Equal(0, await scheduler.ReserveDueRunsAsync(ct));
                Assert.False(await db.ProviderHistorySyncRuns
                    .AnyAsync(x => x.ProviderProductId == product, ct));
            }

            await using (var retry = fixture.CreateDbContext())
            {
                var state = await retry.ProviderHistorySyncStates
                    .SingleAsync(x => x.ProviderProductId == product, ct);
                state.RecordFailure(now.AddMinutes(-16), "Earlier provider failure");
                await retry.SaveChangesAsync(ct);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var scheduler = new ProviderHistoryPeriodicSyncRunScheduler(
                    db, new ProviderHistorySyncRunStarter(db, TimeProvider.System),
                    TimeProvider.System);
                Assert.Equal(1, await scheduler.ReserveDueRunsAsync(ct));
                var run = await db.ProviderHistorySyncRuns.AsNoTracking()
                    .SingleAsync(x => x.ProviderProductId == product, ct);
                Assert.Equal(ProviderHistorySyncRunMode.Incremental, run.Mode);
            }
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

    [Fact]
    public async Task Reservation_persists_attempt_time_before_first_provider_page()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"periodic-attempt-{Guid.NewGuid():N}";
        var previousSuccess = DateTimeOffset.UtcNow.AddHours(-1);
        var beforeReservation = DateTimeOffset.UtcNow;

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                var state = new ProviderHistorySyncState(Guid.NewGuid(), product, 10);
                state.RecordSyncCompleted(previousSuccess);
                seed.ProviderHistorySyncStates.Add(state);
                await seed.SaveChangesAsync(ct);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var scheduler = new ProviderHistoryPeriodicSyncRunScheduler(
                    db, new ProviderHistorySyncRunStarter(db, TimeProvider.System),
                    TimeProvider.System);
                Assert.Equal(1, await scheduler.ReserveDueRunsAsync(ct));
            }

            await using var verify = fixture.CreateDbContext();
            var saved = await verify.ProviderHistorySyncStates.AsNoTracking()
                .SingleAsync(x => x.ProviderProductId == product, ct);
            Assert.Equal(previousSuccess, saved.LastSuccessfulSyncAt);
            Assert.NotNull(saved.LastAttemptAt);
            Assert.InRange(saved.LastAttemptAt.Value, beforeReservation, DateTimeOffset.UtcNow);
            Assert.Equal(0, saved.NextPageNumber);
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
