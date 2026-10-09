using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Vehicles;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserved_run_executor_reuses_the_run_and_records_its_outcome(bool fail)
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"reserved-executor-{Guid.NewGuid():N}";
        var reader = new Reader((page, token) => fail
            ? throw new InvalidOperationException("Reserved history read failed.")
            : Task.FromResult(new ProviderActionHistoryPage([], page, 10, 0)));

        try
        {
            Guid runId;
            await using (var reserve = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(reserve, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                runId = run.Id;
            }

            await using (var db = fixture.CreateDbContext())
            {
                var executor = new ProviderHistoryReservedRunExecutor(
                    db, CreateService(db, reader),
                    new ProviderHistorySyncRunStore(db), TimeProvider.System);
                if (fail)
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        executor.ExecuteAsync(runId, ct));
                else
                    await executor.ExecuteAsync(runId, ct);
            }

            await using var verify = fixture.CreateDbContext();
            var runs = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.ProviderProductId == product).ToListAsync(ct);
            var saved = Assert.Single(runs);
            Assert.Equal(runId, saved.Id);
            Assert.Equal(ProviderHistorySyncRunMode.Manual, saved.Mode);
            Assert.Equal(fail ? ProviderHistorySyncRunStatus.Failed :
                ProviderHistorySyncRunStatus.Succeeded, saved.Status);
            Assert.NotNull(saved.FinishedAt);
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
    public async Task Reserved_run_resumes_at_checkpoint_after_worker_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"restart-sync-{Guid.NewGuid():N}";
        var readPages = new List<int>();
        var reader = new Reader((page, token) =>
        {
            readPages.Add(page);
            return Task.FromResult(new ProviderActionHistoryPage([], page, 10, 0));
        });

        try
        {
            Guid runId;
            await using (var initial = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(initial, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                runId = run.Id;

                var checkpoints = new ProviderHistorySyncStateStore(initial);
                await checkpoints.GetOrCreateAsync(product, 10, ct);
                await checkpoints.RecordPageCompletedAsync(product, 0, TimeProvider.System.GetUtcNow(), ct);
                // Simulate a process crash after committing page 0 but before
                // marking the reserved run completed.
            }

            await using (var restarted = fixture.CreateDbContext())
            {
                var executor = new ProviderHistoryReservedRunExecutor(
                    restarted, CreateService(restarted, reader),
                    new ProviderHistorySyncRunStore(restarted), TimeProvider.System);
                await executor.ExecuteAsync(runId, ct);
            }

            Assert.Equal([1], readPages);
            await using var verify = fixture.CreateDbContext();
            var runStatus = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.Id == runId).Select(x => x.Status).SingleAsync(ct);
            Assert.Equal(ProviderHistorySyncRunStatus.Succeeded, runStatus);
            var checkpoint = await verify.ProviderHistorySyncStates.AsNoTracking()
                .SingleAsync(x => x.ProviderProductId == product, ct);
            Assert.Equal(0, checkpoint.NextPageNumber);
            Assert.NotNull(checkpoint.LastSuccessfulSyncAt);
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
    public async Task Successful_history_import_records_budget_baseline_without_notifications()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var externalProduct = $"budget-history-{suffix}";
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var product = new ParkingProviderProduct(Guid.NewGuid(), externalProduct,
            "History budget", "TEST", "Test", $"LOC-{suffix}", start);
        var period = new ParkingBudgetPeriod(Guid.NewGuid(),
            start.AddDays(-1), start.AddDays(1), TimeSpan.FromHours(1));
        period.AssignProviderProduct(product.Id);
        var rules = new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-2), null,
            TimeSpan.FromHours(4),
            [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))]);
        rules.AssignProviderProduct(product.Id);
        var plate = $"BH{suffix[..6].ToUpperInvariant()}";
        var actionId = $"budget-action-{suffix}";
        var record = new ProviderActionHistoryRecord(actionId, "COMPLETED",
            start, start.AddHours(1), 0.20m, "EUR", plate);
        var reader = new Reader((page, ct) =>
            Task.FromResult(new ProviderActionHistoryPage(
                page == 0 ? [record] : [], page, 10, 1)));
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ParkingBudgetPeriods.Add(period);
                seed.ParkingRuleSets.Add(rules);
                await seed.SaveChangesAsync(token);
            }

            int notificationCountBefore;
            await using (var before = fixture.CreateDbContext())
                notificationCountBefore = await before.NotificationEvents.CountAsync(token);

            await using (var db = fixture.CreateDbContext())
                await CreateService(db, reader).ImportAsync(externalProduct, 10, token);

            await using (var verify = fixture.CreateDbContext())
            {
                Assert.True(await verify.ProviderParkingActions.AnyAsync(
                    x => x.ProviderActionId == actionId && x.VisitId == null, token));
                var thresholds = await verify.ParkingBudgetWarningStates.AsNoTracking()
                    .Where(x => x.ParkingBudgetPeriodId == period.Id)
                    .Select(x => x.ThresholdPercentage).ToArrayAsync(token);
                Assert.NotEmpty(thresholds);
                Assert.Equal(notificationCountBefore,
                    await verify.NotificationEvents.CountAsync(token));
            }

            // Replaying the same history must not duplicate budget thresholds
            // or generate previously suppressed notifications.
            int firstBaselineCount;
            await using (var baseline = fixture.CreateDbContext())
                firstBaselineCount = await baseline.ParkingBudgetWarningStates.CountAsync(
                    x => x.ParkingBudgetPeriodId == period.Id, token);

            await using (var repeat = fixture.CreateDbContext())
                await CreateService(repeat, reader).ImportAsync(externalProduct, 10, token);

            await using (var repeated = fixture.CreateDbContext())
            {
                Assert.Equal(firstBaselineCount, await repeated.ParkingBudgetWarningStates
                    .CountAsync(x => x.ParkingBudgetPeriodId == period.Id, token));
                Assert.Equal(notificationCountBefore,
                    await repeated.NotificationEvents.CountAsync(token));
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingBudgetWarningStates.Where(x => x.ParkingBudgetPeriodId == period.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == externalProduct)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == externalProduct)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
            await cleanup.PaidWindows.Where(x => x.ParkingRuleSetId == rules.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ParkingRuleSets.Where(x => x.Id == rules.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ParkingBudgetPeriods.Where(x => x.Id == period.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_budget_baseline_rejects_missing_or_partial_rules(bool partial)
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var externalId = $"rules-{suffix}";
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var product = new ParkingProviderProduct(Guid.NewGuid(), externalId,
            "Budget test", "TEST", "Test", $"LOC-{suffix}", start);
        var period = new ParkingBudgetPeriod(Guid.NewGuid(), start.AddDays(-1),
            start.AddDays(1), TimeSpan.FromHours(1));
        period.AssignProviderProduct(product.Id);
        var plate = $"RR{suffix[..6].ToUpperInvariant()}";
        var vehicle = Parkeren.Domain.Vehicles.Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), $"action-{suffix}", externalId, product.Location,
            vehicle.Id, Parkeren.Domain.Visits.ProviderActionAssignment.Unassigned,
            start, start.AddHours(1), 0.20m, "COMPLETED", start.AddHours(2));
        ParkingRuleSet? rules = null;
        if (partial)
        {
            rules = new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1),
                start.AddMinutes(30), TimeSpan.FromHours(4),
                [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))]);
            rules.AssignProviderProduct(product.Id);
        }

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ParkingBudgetPeriods.Add(period);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                if (rules is not null)
                    seed.ParkingRuleSets.Add(rules);
                await seed.SaveChangesAsync(ct);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    ProviderHistoryBudgetBaseline.RecordAsync(db, externalId, start.AddHours(3), ct));
                Assert.Contains("Missing historical parking rules", error.Message);
            }

            await using var check = fixture.CreateDbContext();
            Assert.False(await check.ParkingBudgetWarningStates
                .AnyAsync(x => x.ParkingBudgetPeriodId == period.Id, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingBudgetWarningStates.Where(x => x.ParkingBudgetPeriodId == period.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(ct);
            if (rules is not null)
            {
                await cleanup.PaidWindows.Where(x => x.ParkingRuleSetId == rules.Id)
                    .ExecuteDeleteAsync(ct);
                await cleanup.ParkingRuleSets.Where(x => x.Id == rules.Id)
                    .ExecuteDeleteAsync(ct);
            }
            await cleanup.ParkingBudgetPeriods.Where(x => x.Id == period.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(ct);
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
