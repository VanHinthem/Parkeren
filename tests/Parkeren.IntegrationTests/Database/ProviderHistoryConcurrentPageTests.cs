using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryConcurrentPageTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Two_imports_of_same_first_page_commit_only_once()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"parallel-{suffix}";
        var plate = $"PC{suffix[..6].ToUpperInvariant()}";
        var actionId = $"parallel-action-{suffix}";
        var start = DateTimeOffset.UnixEpoch.AddDays(20000);
        var record = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", start, start.AddMinutes(10),
            0.2m, "EUR", plate);
        var page = new ProviderActionHistoryPage([record], 0, 1, 1);

        async Task<Exception?> AttemptAsync()
        {
            await using var db = fixture.CreateDbContext();
            var handler = new ProviderHistoryTransactionalPageImporter(db,
                new ProviderHistoryPageImporter(
                    new ProviderHistoryExistingActionStore(db),
                    new ProviderHistoryNewActionStore(db)),
                new ProviderHistorySyncStateStore(db));

            try
            {
                await handler.ImportPageAsync(product, page, start.AddHours(1), token);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                return ex;
            }
        }

        try
        {
            var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
            Assert.Single(outcomes, x => x is null);
            Assert.Single(outcomes, x => x is InvalidOperationException);

            await using var verify = fixture.CreateDbContext();
            Assert.Equal(1, await verify.ProviderParkingActions.CountAsync(
                x => x.ProviderActionId == actionId, token));
            Assert.Equal(1, await verify.ProviderHistorySyncStates
                .Where(x => x.ProviderProductId == product)
                .Select(x => x.NextPageNumber).SingleAsync(token));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }
}
