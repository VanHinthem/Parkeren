using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Users;
using Parkeren.Domain.Rules;
using Parkeren.Domain.ParkingProvider;
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
            var oldest = await query.GetAsync(new AdminProviderActionHistoryFilter(
                Page: 1, PageSize: 1, ProviderProductId: product,
                OldestFirst: true), ct);
            Assert.Equal(2, oldest.TotalCount);
            Assert.Equal(actions[0].Id, Assert.Single(oldest.Items).Id);

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
    public async Task History_query_resolves_managed_visit_owner_and_imported_assignee()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var owner = new User(Guid.NewGuid(), $"owner-{suffix}", $"OWNER-{suffix}", "hash", UserRole.Visitor);
        var assignee = new User(Guid.NewGuid(), $"assignee-{suffix}", $"ASSIGNEE-{suffix}", "hash", UserRole.Visitor);
        var plate = $"HU{suffix[..6].ToUpperInvariant()}";
        var vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), owner.Id, vehicle.Id,
            owner.Id, start, start.AddHours(1),
            new EffectiveParkingPolicySnapshot(null, null, true, false));
        var managed = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, start, start.AddHours(1));
        var imported = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), $"assigned-{suffix}", $"product-{suffix}", "OSS_J",
            vehicle.Id, ProviderActionAssignment.Manual(assignee.Id),
            start.AddHours(1), start.AddHours(2), 0.2m, "COMPLETED", start.AddHours(3));
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.AddRange(owner, assignee);
                seed.Vehicles.Add(vehicle);
                seed.Visits.Add(visit);
                seed.ProviderParkingActions.AddRange(managed, imported);
                await seed.SaveChangesAsync(ct);
            }

            await using var db = fixture.CreateDbContext();
            var rows = await new AdminProviderActionHistoryQuery(db).GetAsync(
                new AdminProviderActionHistoryFilter(Search: plate), ct);
            var managedRow = Assert.Single(rows.Items, row => row.Id == managed.Id);
            var importedRow = Assert.Single(rows.Items, row => row.Id == imported.Id);
            Assert.Equal(owner.Username, managedRow.Username);
            Assert.Null(managedRow.AssignedUserId);
            Assert.Equal(assignee.Username, importedRow.Username);
            Assert.Equal(assignee.Id, importedRow.AssignedUserId);
            var ownerResults = await new AdminProviderActionHistoryQuery(db).GetAsync(
                new AdminProviderActionHistoryFilter(AssignedUserId: owner.Id, Search: plate), ct);
            Assert.Equal(managed.Id, Assert.Single(ownerResults.Items).Id);
            var assigneeResults = await new AdminProviderActionHistoryQuery(db).GetAsync(
                new AdminProviderActionHistoryFilter(AssignedUserId: assignee.Id, Search: plate), ct);
            Assert.Equal(imported.Id, Assert.Single(assigneeResults.Items).Id);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.Id == managed.Id || x.Id == imported.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(ct, visit.Id);
            await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == owner.Id || x.Id == assignee.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task History_query_filters_open_discrepancies_without_including_resolved_ones()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HD{suffix[..6].ToUpperInvariant()}";
        var vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
        var start = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var openAction = Import($"open-{suffix}", $"product-{suffix}", vehicle.Id, start);
        var resolvedAction = Import($"resolved-{suffix}", $"product-{suffix}", vehicle.Id, start.AddHours(1));
        var open = new ProviderDiscrepancy(Guid.NewGuid(), $"open-{suffix}",
            ProviderDiscrepancyType.ProviderActionStatusMismatch, Guid.NewGuid(),
            start.AddDays(1), providerParkingActionId: openAction.Id);
        var resolved = new ProviderDiscrepancy(Guid.NewGuid(), $"resolved-{suffix}",
            ProviderDiscrepancyType.ProviderActionStatusMismatch, Guid.NewGuid(),
            start.AddDays(1), providerParkingActionId: resolvedAction.Id);
        resolved.Resolve(start.AddDays(2));
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.AddRange(openAction, resolvedAction);
                seed.ProviderDiscrepancies.AddRange(open, resolved);
                await seed.SaveChangesAsync(ct);
            }
            await using var db = fixture.CreateDbContext();
            var query = new AdminProviderActionHistoryQuery(db);
            var withOpen = await query.GetAsync(new AdminProviderActionHistoryFilter(
                Search: plate, HasOpenDiscrepancy: true), ct);
            var withoutOpen = await query.GetAsync(new AdminProviderActionHistoryFilter(
                Search: plate, HasOpenDiscrepancy: false), ct);
            Assert.Equal(openAction.Id, Assert.Single(withOpen.Items).Id);
            Assert.Equal(resolvedAction.Id, Assert.Single(withoutOpen.Items).Id);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderDiscrepancies.Where(x => x.Id == open.Id || x.Id == resolved.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.Id == openAction.Id || x.Id == resolvedAction.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
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

    private static Parkeren.Domain.Visits.ProviderParkingAction Import(
        string id, string product, Guid vehicleId, DateTimeOffset start) =>
        Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(Guid.NewGuid(), id, product, "OSS_J",
            vehicleId, ProviderActionAssignment.Unassigned, start, start.AddMinutes(30),
            0.2m, "COMPLETED", start.AddHours(3));
}
