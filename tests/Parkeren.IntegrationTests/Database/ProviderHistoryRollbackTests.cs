using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryRollbackTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Invalid_second_record_rolls_back_first_record_and_checkpoint()
    {
        var token = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString("N");
        var product = $"rollback-{key}";
        var plate = $"RB{key[..6].ToUpperInvariant()}";
        var start = DateTimeOffset.UnixEpoch.AddDays(20000);
        var good = new ProviderActionHistoryRecord($"good-{key}", "COMPLETED",
            start, start.AddMinutes(10), 0.2m, "EUR", plate);
        var bad = good with { ProviderActionId = $"bad-{key}", Status = "ACTIVE" };

        await using (var db = fixture.CreateDbContext())
        {
            var transaction = new ProviderHistoryTransactionalPageImporter(
                db,
                new ProviderHistoryPageImporter(
                    new ProviderHistoryExistingActionStore(db),
                    new ProviderHistoryNewActionStore(db)),
                new ProviderHistorySyncStateStore(db));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transaction.ImportPageAsync(product,
                    new ProviderActionHistoryPage([good, bad], 0, 10, 2),
                    start.AddHours(1), token));
        }

        await using var check = fixture.CreateDbContext();
        Assert.False(await check.ProviderParkingActions.AnyAsync(
            x => x.ProviderActionId == good.ProviderActionId, token));
        Assert.False(await check.ProviderHistorySyncStates.AnyAsync(
            x => x.ProviderProductId == product, token));
    }
}
