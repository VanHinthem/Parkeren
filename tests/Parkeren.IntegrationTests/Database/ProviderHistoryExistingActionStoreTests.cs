using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Users;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryExistingActionStoreTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_history_does_not_mutate_managed_active_or_reconciling_actions(bool reconciling)
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actionId = $"managed-active-{suffix}";
        var product = $"history-safe-{suffix}";
        var start = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), null,
            start, start.AddHours(1), product);
        action.MarkStarting();
        action.MarkActive(actionId, start, "ACTIVE");
        if (reconciling)
        {
            action.BeginStopping();
            action.MarkUnknown();
            action.BeginReconciliation();
        }

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(ct);
            }

            var fromHistory = new ProviderActionHistoryRecord(
                actionId, "COMPLETED", start, start.AddMinutes(40), 1.25m, "EUR");
            await using (var import = fixture.CreateDbContext())
            {
                var result = await new ProviderHistoryExistingActionStore(import)
                    .ApplyIfExistingAsync(product, fromHistory, start.AddHours(2), ct);
                Assert.Equal(ProviderHistoryExistingActionResult.SkippedManaged, result);
            }

            await using var verify = fixture.CreateDbContext();
            var saved = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.Id == action.Id, ct);
            Assert.Equal(action.State, saved.State);
            Assert.Equal(action.Health, saved.Health);
            Assert.Equal(start, saved.ActualStartAt);
            Assert.Null(saved.ActualEndAt);
            Assert.Null(saved.ProviderCostAmount);
            Assert.Equal("ACTIVE", saved.ProviderStatus);
            Assert.Equal(action.VisitId, saved.VisitId);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Finalized_managed_history_requires_completed_visit_and_no_inflight_operation(
        bool visitActive, bool pendingOperation, bool shouldRefresh)
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"managed-revisit-{suffix}";
        var providerId = $"managed-action-{suffix}";
        var plate = $"MC{suffix[..6].ToUpperInvariant()}";
        var start = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var observedAt = start.AddDays(1);
        var user = new User(Guid.NewGuid(), $"managed-{suffix}", $"MANAGED-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, start.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        if (!visitActive)
        {
            visit.BeginStopping();
            visit.Complete(start.AddHours(1));
        }

        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id,
            start, start.AddHours(1), product);
        action.MarkStarting();
        action.MarkActive(providerId, start, "ACTIVE");
        action.MarkCompleted(start.AddHours(1));
        action.SetInitialProviderCost(0.30m);
        action.ScheduleHistoryReconciliation();
        action.ApplyProviderHistory(start, start.AddHours(1), 0.30m);

        ProviderOperation? operation = pendingOperation
            ? new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visit.Id, action.Id, ProviderOperationType.Stop)
            : null;
        var corrected = new ProviderActionHistoryRecord(providerId, "COMPLETED",
            start.AddMinutes(1), start.AddMinutes(58), 0.28m, "EUR");

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.Add(user);
                seed.Vehicles.Add(vehicle);
                seed.Visits.Add(visit);
                seed.ProviderParkingActions.Add(action);
                if (operation is not null)
                    seed.ProviderOperations.Add(operation);
                await seed.SaveChangesAsync(ct);
            }

            await using (var db = fixture.CreateDbContext())
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var result = await new ProviderHistoryExistingActionStore(db)
                    .ApplyIfExistingAsync(product, corrected, observedAt, ct);
                Assert.Equal(shouldRefresh
                    ? ProviderHistoryExistingActionResult.RefreshedManaged
                    : ProviderHistoryExistingActionResult.SkippedManaged, result);
                await transaction.CommitAsync(ct);
            }

            await using var verify = fixture.CreateDbContext();
            var persisted = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.Id == action.Id, ct);
            Assert.Equal(shouldRefresh ? corrected.ActualStartAt : start, persisted.ActualStartAt);
            Assert.Equal(shouldRefresh ? corrected.ActualEndAt : start.AddHours(1), persisted.ActualEndAt);
            Assert.Equal(shouldRefresh ? 0.28m : 0.30m, persisted.ProviderCostAmount);
            Assert.Equal(visit.Id, persisted.VisitId);
            Assert.Equal(ProviderActionOrigin.Managed, persisted.Origin);
            Assert.Equal(ProviderActionState.Completed, persisted.State);
            Assert.Equal(ProviderHistoryStatus.Reconciled, persisted.HistoryStatus);
            Assert.Equal(shouldRefresh ? "COMPLETED" : "ACTIVE", persisted.ProviderStatus);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderOperations.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(ct);
            await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
        }
    }

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
    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("STOPPING")]
    [InlineData("FAILED")]
    public async Task Non_completed_history_cannot_refresh_an_imported_action(string status)
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HS{suffix[..6].ToUpperInvariant()}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var started = new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
        var actionId = $"non-final-{suffix}";
        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), actionId, "history-product", "OSS Zone J",
            vehicle.Id, ProviderActionAssignment.Unassigned,
            started, started.AddMinutes(20), 0.25m, "COMPLETED", started.AddHours(1));
        var conflicting = new ProviderActionHistoryRecord(
            actionId, status, started.AddMinutes(1),
            started.AddMinutes(30), 0.90m, "EUR", plate, "OSS Zone J");

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
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new ProviderHistoryExistingActionStore(db).ApplyIfExistingAsync(
                        "history-product", conflicting, started.AddHours(2), token));
            }

            await using var verify = fixture.CreateDbContext();
            var saved = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.Id == action.Id, token);
            Assert.Equal(started, saved.ActualStartAt);
            Assert.Equal(started.AddMinutes(20), saved.ActualEndAt);
            Assert.Equal(0.25m, saved.ProviderCostAmount);
            Assert.Equal("COMPLETED", saved.ProviderStatus);
            Assert.Equal(ProviderHistoryStatus.Reconciled, saved.HistoryStatus);
            Assert.Equal(started.AddHours(1), saved.LastSyncedAt);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(token);
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
