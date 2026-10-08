using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Administration;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderActionAttributionPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Imported_attribution_and_manual_audit_survive_database_round_trip()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.AddHours(-2);
        var user = new User(Guid.NewGuid(), $"history-{suffix}", $"HISTORY-{suffix}", "hash", UserRole.Visitor);
        var actor = new User(Guid.NewGuid(), $"history-actor-{suffix}",
            $"HISTORY-ACTOR-{suffix}", "hash", UserRole.Admin);
        var plate = $"HX{suffix[..6].ToUpperInvariant()}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), $"history-{suffix}", $"product-{suffix}", "OSS Zone J",
            vehicle.Id, ProviderActionAssignment.InferFromVehicleUsers([user.Id]),
            now, now.AddMinutes(20), 0.15m, "COMPLETED", now.AddHours(1));
        var audit = ProviderActionAssignmentAudit.Create(action.Id, actor.Id,
            ProviderActionAssignment.InferFromVehicleUsers([user.Id]),
            ProviderActionAssignment.Manual(actor.Id), now.AddHours(2));

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.AddRange(user, actor);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(token);
            }

            await using (var update = fixture.CreateDbContext())
            {
                var persisted = await update.ProviderParkingActions
                    .SingleAsync(x => x.Id == action.Id, token);
                Assert.Equal(ProviderActionOrigin.Imported, persisted.Origin);
                Assert.Equal(ProviderActionAssignmentSource.Inferred, persisted.AssignmentSource);
                Assert.Equal(user.Id, persisted.AssignedUserId);
                Assert.Equal(vehicle.Id, persisted.VehicleId);
                Assert.Null(persisted.VisitId);
                Assert.Equal(now.AddHours(1), persisted.LastSyncedAt);

                persisted.AssignHistoricalUser(ProviderActionAssignment.Manual(actor.Id));
                update.AdminAuditEvents.Add(audit);
                await update.SaveChangesAsync(token);
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var persisted = await verify.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.Id == action.Id, token);
                Assert.Equal(actor.Id, persisted.AssignedUserId);
                Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, persisted.AssignmentSource);
                Assert.Equal(vehicle.Id, persisted.VehicleId);
                Assert.Equal(ProviderActionOrigin.Imported, persisted.Origin);

                var persistedAudit = await verify.AdminAuditEvents.AsNoTracking()
                    .SingleAsync(x => x.Id == audit.Id, token);
                Assert.Equal(actor.Id, persistedAudit.ActorUserId);
                Assert.Equal(action.Id.ToString("D"), persistedAudit.TargetId);
                Assert.Contains(user.Id.ToString("D"), persistedAudit.ContextJson);
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.Id == audit.Id).ExecuteDeleteAsync(token);
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(token);
            await cleanup.Users.Where(x => x.Id == user.Id || x.Id == actor.Id).ExecuteDeleteAsync(token);
        }
    }
}
