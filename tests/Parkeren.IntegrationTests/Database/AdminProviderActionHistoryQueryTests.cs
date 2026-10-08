using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminProviderActionHistoryQueryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task History_query_filters_by_product_and_plate_and_paginates_in_start_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"history-query-{suffix}";
        var otherProduct = $"other-query-{suffix}";
        var plate = $"HQ{suffix[..6].ToUpperInvariant()}";
        var vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var actions = new[]
        {
            Import($"first-{suffix}", product, vehicle.Id, start),
            Import($"second-{suffix}", product, vehicle.Id, start.AddHours(1)),
            Import($"other-{suffix}", otherProduct, vehicle.Id, start.AddHours(2))
        };

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.AddRange(actions);
                await seed.SaveChangesAsync(ct);
            }

            await using var db = fixture.CreateDbContext();
            var query = new AdminProviderActionHistoryQuery(db);
            var first = await query.GetAsync(new AdminProviderActionHistoryFilter(
                Page: 1, PageSize: 1, Search: plate.ToLowerInvariant(),
                ProviderProductId: product, Origin: ProviderActionOrigin.Imported), ct);
            var second = await query.GetAsync(new AdminProviderActionHistoryFilter(
                Page: 2, PageSize: 1, Search: plate,
                ProviderProductId: product, Origin: ProviderActionOrigin.Imported), ct);

            Assert.Equal(2, first.TotalCount);
            Assert.Equal(2, second.TotalCount);
            Assert.Single(first.Items);
            Assert.Single(second.Items);
            Assert.Equal(actions[1].Id, first.Items[0].Id);
            Assert.Equal(actions[0].Id, second.Items[0].Id);
            Assert.Equal(plate, first.Items[0].LicensePlate);
            Assert.All(first.Items.Concat(second.Items), row =>
                Assert.Equal(product, row.ProviderProductId));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            var ids = actions.Select(x => x.Id).ToArray();
            await cleanup.ProviderParkingActions.Where(x => ids.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task History_query_rejects_invalid_page_parameters()
    {
        await using var db = fixture.CreateDbContext();
        var query = new AdminProviderActionHistoryQuery(db);
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            query.GetAsync(new AdminProviderActionHistoryFilter(Page: 0), ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            query.GetAsync(new AdminProviderActionHistoryFilter(PageSize: 101), ct));
    }

    private static ProviderParkingAction Import(
        string id, string product, Guid vehicleId, DateTimeOffset start) =>
        ProviderParkingAction.ImportCompleted(Guid.NewGuid(), id, product, "OSS_J",
            vehicleId, ProviderActionAssignment.Unassigned, start, start.AddMinutes(30),
            0.2m, "COMPLETED", start.AddHours(3));
}
