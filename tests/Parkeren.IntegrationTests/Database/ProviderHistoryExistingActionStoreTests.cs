using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryExistingActionStoreTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Repeat_import_updates_provider_facts_but_preserves_manual_user()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HR{suffix[..6].ToUpperInvariant()}";
        var user = new User(Guid.NewGuid(), $"repeat-{suffix}", $"REPEAT-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var started = new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
        var actionId = $"repeat-{suffix}";
        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), actionId, "history-product", "OSS Zone J",
            vehicle.Id, ProviderActionAssignment.Unassigned,
            started, started.AddMinutes(20), 0.25m, "COMPLETED", started.AddHours(1));
        action.AssignHistoricalUser(ProviderActionAssignment.Manual(user.Id));
        var revised = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", started.AddMinutes(1),
            started.AddMinutes(21), 0.30m, "EUR", plate, "OSS Zone J");

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.Add(user);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var update = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryExistingActionStore(update);
                Assert.Equal(ProviderHistoryExistingActionResult.RefreshedImported,
                    await store.ApplyIfExistingAsync(
                        "history-product", revised, started.AddHours(2), token));
                Assert.Equal(ProviderHistoryExistingActionResult.RefreshedImported,
                    await store.ApplyIfExistingAsync(
                        "history-product", revised, started.AddHours(2), token));
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var persisted = await verify.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.ProviderActionId == actionId, token);
                Assert.Equal(started.AddMinutes(1), persisted.ActualStartAt);
                Assert.Equal(started.AddMinutes(21), persisted.ActualEndAt);
                Assert.Equal(0.30m, persisted.ProviderCostAmount);
                Assert.Equal(user.Id, persisted.AssignedUserId);
                Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, persisted.AssignmentSource);
                Assert.Equal(vehicle.Id, persisted.VehicleId);
                Assert.Null(persisted.VisitId);
                Assert.Equal(1, await verify.ProviderParkingActions.CountAsync(
                    x => x.ProviderActionId == actionId, token));
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(token);
            await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(token);
        }
    }
    [Fact]
    public async Task Same_action_id_from_another_product_is_rejected_without_modifying_history()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HP{suffix[..6].ToUpperInvariant()}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var started = new DateTimeOffset(2026, 10, 7, 11, 0, 0, TimeSpan.Zero);
        var actionId = $"product-conflict-{suffix}";
        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), actionId, "original-product", "OSS Zone J",
            vehicle.Id, ProviderActionAssignment.Unassigned,
            started, started.AddMinutes(20), 0.25m, "COMPLETED", started.AddHours(1));
        var conflicting = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", started.AddMinutes(2),
            started.AddMinutes(22), 0.50m, "EUR", plate, "OSS Zone J");

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryExistingActionStore(db);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    store.ApplyIfExistingAsync(
                        "another-product", conflicting, started.AddHours(2), token));
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var persisted = await verify.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.ProviderActionId == actionId, token);
                Assert.Equal("original-product", persisted.ProviderProductId);
                Assert.Equal(started, persisted.ActualStartAt);
                Assert.Equal(started.AddMinutes(20), persisted.ActualEndAt);
                Assert.Equal(0.25m, persisted.ProviderCostAmount);
                Assert.Equal(1, await verify.ProviderParkingActions.CountAsync(
                    x => x.ProviderActionId == actionId, token));
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(token);
        }
    }

}
