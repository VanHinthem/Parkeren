using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryAssignmentServiceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Manual_assignment_and_unassignment_are_persisted_and_audited_once()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"HA{suffix[..6].ToUpperInvariant()}";
        var actor = new User(Guid.NewGuid(), $"actor-{suffix}", $"ACTOR-{suffix}", "hash", UserRole.Admin);
        var assignee = new User(Guid.NewGuid(), $"assignee-{suffix}", $"ASSIGNEE-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var start = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var action = ProviderParkingAction.ImportCompleted(Guid.NewGuid(), $"audit-{suffix}",
            "history-product", "OSS Zone J", vehicle.Id, ProviderActionAssignment.Unassigned,
            start, start.AddMinutes(30), 0.25m, "COMPLETED", start.AddHours(1));

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.AddRange(actor, assignee);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var change = fixture.CreateDbContext())
            {
                var service = new ProviderHistoryAssignmentService(change, TimeProvider.System);
                Assert.True(await service.AssignAsync(action.Id, actor.Id, assignee.Id, token));
                Assert.False(await service.AssignAsync(action.Id, actor.Id, assignee.Id, token));
                Assert.True(await service.AssignAsync(action.Id, actor.Id, null, token));
                Assert.False(await service.AssignAsync(action.Id, actor.Id, null, token));
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var saved = await verify.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.Id == action.Id, token);
                Assert.Null(saved.AssignedUserId);
                Assert.Equal(ProviderActionAssignmentSource.Unassigned, saved.AssignmentSource);
                var events = await verify.AdminAuditEvents.AsNoTracking()
                    .Where(x => x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                                x.TargetId == action.Id.ToString("D"))
                    .OrderBy(x => x.CreatedAt).ToListAsync(token);
                Assert.Equal(2, events.Count);
                Assert.All(events, e => Assert.Equal(actor.Id, e.ActorUserId));
                using var first = JsonDocument.Parse(events[0].ContextJson!);
                Assert.Equal(assignee.Id.ToString("D"), first.RootElement.GetProperty("NewUserId").GetString());
                using var second = JsonDocument.Parse(events[1].ContextJson!);
                Assert.Equal("Unassigned", second.RootElement.GetProperty("NewSource").GetString());
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                x.TargetId == action.Id.ToString("D")).ExecuteDeleteAsync(token);
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(token);
            await cleanup.Users.Where(x => x.Id == actor.Id || x.Id == assignee.Id).ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Managed_action_rejects_assignment_without_audit()
    {
        var token = TestContext.Current.CancellationToken;
        var start = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var action = new ProviderParkingAction(Guid.NewGuid(), null, start, start.AddMinutes(30));
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var service = new ProviderHistoryAssignmentService(db, TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.AssignAsync(action.Id, Guid.NewGuid(), null, token));
                Assert.False(await db.AdminAuditEvents.AnyAsync(
                    x => x.TargetId == action.Id.ToString("D") &&
                         x.TargetType == ProviderActionAssignmentAudit.TargetName, token));
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(token);
        }
    }
    [Fact]
    public async Task Inactive_admin_cannot_assign_historical_action_or_create_audit()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"inactive-{suffix}",
            $"INACTIVE-{suffix}", "hash", UserRole.Admin);
        admin.Deactivate();
        var plate = $"IA{suffix[..6].ToUpperInvariant()}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var start = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), $"inactive-action-{suffix}", "history-product",
            "OSS Zone J", vehicle.Id, ProviderActionAssignment.Unassigned,
            start, start.AddMinutes(30), 0.25m, "COMPLETED", start.AddHours(1));

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.Add(admin);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var service = new ProviderHistoryAssignmentService(db, TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.AssignAsync(action.Id, admin.Id, null, token));
                Assert.False(await db.AdminAuditEvents.AnyAsync(
                    x => x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                         x.TargetId == action.Id.ToString("D"), token));
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(token);
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Visitor_cannot_assign_historical_action_or_create_audit()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var plate = $"AV{suffix[..6].ToUpperInvariant()}";
        var visitor = new User(Guid.NewGuid(), $"visitor-{suffix}",
            $"VISITOR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var start = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), $"visitor-action-{suffix}", "history-product",
            "OSS Zone J", vehicle.Id, ProviderActionAssignment.Unassigned,
            start, start.AddMinutes(30), 0.25m, "COMPLETED", start.AddHours(1));

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.Add(visitor);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var service = new ProviderHistoryAssignmentService(db, TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.AssignAsync(action.Id, visitor.Id, visitor.Id, token));
            }

            await using (var check = fixture.CreateDbContext())
            {
                var saved = await check.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.Id == action.Id, token);
                Assert.Null(saved.AssignedUserId);
                Assert.Equal(ProviderActionAssignmentSource.Unassigned, saved.AssignmentSource);
                Assert.False(await check.AdminAuditEvents.AnyAsync(
                    x => x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                         x.TargetId == action.Id.ToString("D"), token));
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.Users.Where(x => x.Id == visitor.Id)
                .ExecuteDeleteAsync(token);
        }
    }

}
