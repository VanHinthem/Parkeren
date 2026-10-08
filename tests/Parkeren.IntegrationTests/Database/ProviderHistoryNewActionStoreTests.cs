using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryNewActionStoreTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Repeated_import_creates_one_action_and_one_inactive_vehicle()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HI{suffix[..6].ToUpperInvariant()}";
        var actionId = $"import-{suffix}";
        var start = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var record = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", start, start.AddMinutes(35),
            0.40m, "EUR", plate, "OSS Zone J");

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryNewActionStore(db);
                Assert.Equal(ProviderHistoryNewActionResult.Inserted,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(1), token));
                Assert.Equal(ProviderHistoryNewActionResult.AlreadyExists,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(2), token));
            }

            await using var verify = fixture.CreateDbContext();
            var action = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.ProviderActionId == actionId, token);
            var vehicle = await verify.Vehicles.AsNoTracking()
                .SingleAsync(x => x.Id == action.VehicleId, token);

            Assert.Null(action.VisitId);
            Assert.Equal(ProviderActionOrigin.Imported, action.Origin);
            Assert.Equal(ProviderActionAssignmentSource.Unassigned, action.AssignmentSource);
            Assert.Null(action.AssignedUserId);
            Assert.Equal(VehicleStatus.Inactive, vehicle.Status);
            Assert.Equal(plate, vehicle.NormalizedLicensePlate);
            Assert.False(await verify.UserVehicles.AnyAsync(x => x.VehicleId == vehicle.Id, token));
            Assert.Equal(1, await verify.ProviderParkingActions.CountAsync(
                x => x.ProviderActionId == actionId, token));
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Existing_vehicle_with_single_user_is_inferred_only_on_first_import()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HV{suffix[..6].ToUpperInvariant()}";
        var actionId = $"linked-{suffix}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var user = new User(Guid.NewGuid(), $"history-{suffix}", $"HISTORY-{suffix}", "hash", UserRole.Visitor);
        var start = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var record = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", start, start.AddMinutes(15),
            0.20m, "EUR", plate, "OSS Zone J");

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                db.Users.Add(user);
                db.Vehicles.Add(vehicle);
                db.UserVehicles.Add(new UserVehicle(user.Id, vehicle.Id));
                await db.SaveChangesAsync(token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryNewActionStore(db);
                Assert.Equal(ProviderHistoryNewActionResult.Inserted,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(1), token));
                Assert.Equal(ProviderHistoryNewActionResult.AlreadyExists,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(2), token));
            }

            await using var verify = fixture.CreateDbContext();
            var action = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.ProviderActionId == actionId, token);
            Assert.Equal(vehicle.Id, action.VehicleId);
            Assert.Equal(user.Id, action.AssignedUserId);
            Assert.Equal(ProviderActionAssignmentSource.Inferred, action.AssignmentSource);
            Assert.Equal(VehicleStatus.Active,
                (await verify.Vehicles.SingleAsync(x => x.Id == vehicle.Id, token)).Status);
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await db.UserVehicles.Where(x => x.VehicleId == vehicle.Id)
                .ExecuteDeleteAsync(token);
            await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(token);
        }
    }
    [Fact]
    public async Task Vehicle_shared_by_two_users_keeps_imported_history_unassigned()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HM{suffix[..6].ToUpperInvariant()}";
        var actionId = $"shared-{suffix}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var userA = new User(Guid.NewGuid(), $"shared-a-{suffix}", $"SHARED-A-{suffix}", "hash", UserRole.Visitor);
        var userB = new User(Guid.NewGuid(), $"shared-b-{suffix}", $"SHARED-B-{suffix}", "hash", UserRole.Visitor);
        var start = new DateTimeOffset(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
        var record = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", start, start.AddMinutes(30),
            0.35m, "EUR", plate, "OSS Zone J");

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                db.Users.AddRange(userA, userB);
                db.Vehicles.Add(vehicle);
                db.UserVehicles.AddRange(
                    new UserVehicle(userA.Id, vehicle.Id),
                    new UserVehicle(userB.Id, vehicle.Id));
                await db.SaveChangesAsync(token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryNewActionStore(db);
                Assert.Equal(ProviderHistoryNewActionResult.Inserted,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(1), token));
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var action = await verify.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.ProviderActionId == actionId, token);
                Assert.Equal(vehicle.Id, action.VehicleId);
                Assert.Null(action.AssignedUserId);
                Assert.Equal(ProviderActionAssignmentSource.Unassigned, action.AssignmentSource);
                Assert.Null(action.VisitId);
                Assert.Equal(2, await verify.UserVehicles.CountAsync(
                    x => x.VehicleId == vehicle.Id, token));
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await db.UserVehicles.Where(x => x.VehicleId == vehicle.Id)
                .ExecuteDeleteAsync(token);
            await db.Users.Where(x => x.Id == userA.Id || x.Id == userB.Id)
                .ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Later_vehicle_user_link_does_not_reassign_existing_historical_action()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HL{suffix[..6].ToUpperInvariant()}";
        var actionId = $"later-{suffix}";
        var user = new User(Guid.NewGuid(), $"later-{suffix}", $"LATER-{suffix}", "hash", UserRole.Visitor);
        var start = new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);
        var record = new ProviderActionHistoryRecord(
            actionId, "COMPLETED", start, start.AddMinutes(10),
            0.15m, "EUR", plate, "OSS Zone J");
        Guid? vehicleId = null;

        try
        {
            await using (var initial = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryNewActionStore(initial);
                Assert.Equal(ProviderHistoryNewActionResult.Inserted,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(1), token));
                vehicleId = await initial.ProviderParkingActions
                    .Where(x => x.ProviderActionId == actionId)
                    .Select(x => x.VehicleId)
                    .SingleAsync(token);
            }

            await using (var assign = fixture.CreateDbContext())
            {
                assign.Users.Add(user);
                assign.UserVehicles.Add(new UserVehicle(user.Id, vehicleId!.Value));
                await assign.SaveChangesAsync(token);
            }

            await using (var repeated = fixture.CreateDbContext())
            {
                var store = new ProviderHistoryNewActionStore(repeated);
                Assert.Equal(ProviderHistoryNewActionResult.AlreadyExists,
                    await store.InsertIfMissingAsync("product-1", record, start.AddHours(2), token));
            }

            await using var verify = fixture.CreateDbContext();
            var action = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.ProviderActionId == actionId, token);
            Assert.Null(action.AssignedUserId);
            Assert.Equal(ProviderActionAssignmentSource.Unassigned, action.AssignmentSource);
            Assert.Equal(vehicleId, action.VehicleId);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            if (vehicleId.HasValue)
            {
                await cleanup.UserVehicles.Where(x => x.VehicleId == vehicleId.Value)
                    .ExecuteDeleteAsync(token);
                await cleanup.Vehicles.Where(x => x.Id == vehicleId.Value)
                    .ExecuteDeleteAsync(token);
            }
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(token);
        }
    }

}
