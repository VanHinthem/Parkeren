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
