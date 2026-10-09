using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryAtomicRunCountsTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Missing_run_rolls_back_imported_action_and_page_checkpoint()
    {
        var token = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString("N");
        var product = $"atomic-{key}";
        var actionId = $"atomic-action-{key}";
        var plate = $"AT{key[..6].ToUpperInvariant()}";
        var start = DateTimeOffset.UnixEpoch.AddDays(20000);
        var record = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", start, start.AddMinutes(10), 0.2m, "EUR", plate);

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var checkpoints = new ProviderHistorySyncStateStore(db);
                var importer = new ProviderHistoryTransactionalPageImporter(
                    db, new ProviderHistoryPageImporter(
                        new ProviderHistoryExistingActionStore(db),
                        new ProviderHistoryNewActionStore(db)),
                    checkpoints, new ProviderHistorySyncRunStore(db));

                // The missing run forces the run-counter write to fail after the
                // provider action and checkpoint have been written in this transaction.
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    importer.ImportPageAsync(product,
                        new ProviderActionHistoryPage([record], 0, 10, 1),
                        start.AddHours(1), token, Guid.NewGuid()));
            }

            await using (var check = fixture.CreateDbContext())
            {
                Assert.False(await check.ProviderParkingActions.AnyAsync(
                    x => x.ProviderActionId == actionId, token));
                Assert.False(await check.ProviderHistorySyncStates.AnyAsync(
                    x => x.ProviderProductId == product, token));
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
        }
    }
}
