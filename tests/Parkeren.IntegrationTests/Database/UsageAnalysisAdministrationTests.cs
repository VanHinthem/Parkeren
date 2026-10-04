using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class UsageAnalysisAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Shared_plate_keeps_usage_attributed_to_each_visitor_and_archived_entities_visible()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAnalysisStateAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor1 = new User(Guid.NewGuid(), $"visitor-a-{suffix}", $"VISITOR-A-{suffix}", "hash", UserRole.Visitor);
        var visitor2 = new User(Guid.NewGuid(), $"visitor-b-{suffix}", $"VISITOR-B-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "12-AB-34", "12AB34", "Gedeeld");

        var offset = TimeSpan.FromHours(2);
        var firstStart = new DateTimeOffset(2026, 9, 28, 10, 0, 0, offset).ToUniversalTime();
        var firstEnd = new DateTimeOffset(2026, 9, 28, 11, 0, 0, offset).ToUniversalTime();
        var secondStart = firstEnd;
        var secondEnd = new DateTimeOffset(2026, 9, 28, 12, 30, 0, offset).ToUniversalTime();

        var firstVisit = CompletedVisit(visitor1.Id, vehicle.Id, firstStart, firstEnd);
        var secondVisit = CompletedVisit(visitor2.Id, vehicle.Id, secondStart, secondEnd);
        var firstAction = CompletedProviderAction(firstVisit.Id, firstStart, firstEnd);
        var secondAction = CompletedProviderAction(secondVisit.Id, secondStart, secondEnd);

        visitor2.Deactivate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor1, visitor2);
            seed.Vehicles.Add(vehicle);
            seed.Visits.AddRange(firstVisit, secondVisit);
            seed.ProviderParkingActions.AddRange(firstAction, secondAction);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            Assert.True(await administration.Service.ArchiveUserAsync(admin.Id, visitor1.Id, ct));
            Assert.True(await administration.Service.ArchiveVehicleAsync(admin.Id, vehicle.Id, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.DeleteUserAsync(admin.Id, visitor1.Id, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.DeleteVehicleAsync(admin.Id, vehicle.Id, ct));

            var analysis = await administration.Service.GetUsageAnalysisAsync(
                admin.Id,
                firstStart.AddMinutes(-1),
                secondEnd.AddMinutes(1),
                ct);

            Assert.Equal(2, analysis.ByUser.Count);

            var archivedVisitor = Assert.Single(analysis.ByUser, x => x.UserId == visitor1.Id);
            Assert.True(archivedVisitor.IsArchived);
            Assert.Equal(1, archivedVisitor.VisitCount);
            Assert.Equal(60, archivedVisitor.PaidDurationMinutes);
            Assert.Equal(2m, archivedVisitor.Amount);
            Assert.Single(archivedVisitor.Visits);
            Assert.Equal(firstVisit.Id, archivedVisitor.Visits[0].VisitId);

            var activeVisitor = Assert.Single(analysis.ByUser, x => x.UserId == visitor2.Id);
            Assert.False(activeVisitor.IsArchived);
            Assert.Equal(1, activeVisitor.VisitCount);
            Assert.Equal(90, activeVisitor.PaidDurationMinutes);
            Assert.Equal(3m, activeVisitor.Amount);
            Assert.Single(activeVisitor.Visits);
            Assert.Equal(secondVisit.Id, activeVisitor.Visits[0].VisitId);

            var plate = Assert.Single(analysis.ByLicensePlate);
            Assert.Equal("12AB34", plate.Label);
            Assert.True(plate.IsArchived);
            Assert.Equal(2, plate.VisitCount);
            Assert.Equal(150, plate.PaidDurationMinutes);
            Assert.Equal(5m, plate.Amount);
            Assert.Equal(2, plate.Visits.Count);
            Assert.Contains(plate.Visits, x => x.UserId == visitor1.Id);
            Assert.Contains(plate.Visits, x => x.UserId == visitor2.Id);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions
                .Where(x => x.VisitId == firstVisit.Id || x.VisitId == secondVisit.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(ct, firstVisit.Id, secondVisit.Id);
            await cleanup.Visits.Where(x => x.Id == firstVisit.Id || x.Id == secondVisit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitor1.Id || x.Id == visitor2.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingTariffs.ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Active_visit_blocks_archiving_and_deleting_user_or_vehicle()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"lifecycle-admin-{suffix}", $"LIFECYCLE-ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"lifecycle-visitor-{suffix}", $"LIFECYCLE-VISITOR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"{suffix[..2]}-{suffix[2..6]}", suffix[..6], "Active visit");
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            visitor.Id,
            vehicle.Id,
            visitor.Id,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            null,
            new EffectiveParkingPolicySnapshot(null, null, true, false));
        visit.Activate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.ArchiveUserAsync(admin.Id, visitor.Id, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.ArchiveVehicleAsync(admin.Id, vehicle.Id, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.DeleteUserAsync(admin.Id, visitor.Id, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.DeleteVehicleAsync(admin.Id, vehicle.Id, ct));

            await using var verify = fixture.CreateDbContext();
            Assert.Equal(UserStatus.Active, await verify.Users.Where(x => x.Id == visitor.Id).Select(x => x.Status).SingleAsync(ct));
            Assert.Equal(VehicleStatus.Active, await verify.Vehicles.Where(x => x.Id == vehicle.Id).Select(x => x.Status).SingleAsync(ct));
            Assert.Equal(VisitStatus.Active, await verify.Visits.Where(x => x.Id == visit.Id).Select(x => x.Status).SingleAsync(ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(ct, visit.Id);
            await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitor.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Records_without_parking_history_can_be_deleted_with_operational_dependents()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"delete-admin-{suffix}", $"DELETE-ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"delete-visitor-{suffix}", $"DELETE-VISITOR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"{suffix[..2]}-{suffix[2..6]}", suffix[..6], "No history");
        var session = new UserSession(
            Guid.NewGuid(), visitor.Id, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor);
            seed.Vehicles.Add(vehicle);
            seed.UserSessions.Add(session);
            seed.UserVehicles.Add(new UserVehicle(visitor.Id, vehicle.Id));
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            var users = await administration.Service.GetUsersAsync(admin.Id, ct);
            Assert.True(Assert.Single(users, x => x.Id == visitor.Id).CanDelete);
            var vehicles = await administration.Service.GetVehiclesAsync(admin.Id, ct);
            Assert.True(Assert.Single(vehicles, x => x.Id == vehicle.Id).CanDelete);

            Assert.True(await administration.Service.DeleteUserAsync(admin.Id, visitor.Id, ct));
            await using (var verifyUser = fixture.CreateDbContext())
            {
                Assert.False(await verifyUser.Users.AnyAsync(x => x.Id == visitor.Id, ct));
                Assert.False(await verifyUser.UserSessions.AnyAsync(x => x.UserId == visitor.Id, ct));
                Assert.False(await verifyUser.UserVehicles.AnyAsync(x => x.UserId == visitor.Id, ct));
            }

            Assert.True(await administration.Service.DeleteVehicleAsync(admin.Id, vehicle.Id, ct));
            await using var verifyVehicle = fixture.CreateDbContext();
            Assert.False(await verifyVehicle.Vehicles.AnyAsync(x => x.Id == vehicle.Id, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.UserSessions.Where(x => x.UserId == visitor.Id).ExecuteDeleteAsync(ct);
            await cleanup.UserVehicles.Where(x => x.UserId == visitor.Id || x.VehicleId == vehicle.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitor.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("actor")]
    [InlineData("vehicle")]
    public async Task Start_claim_rechecks_status_after_lifecycle_change(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actor = new User(Guid.NewGuid(), $"claim-admin-{suffix}", $"CLAIM-ADMIN-{suffix}", "hash", UserRole.Admin);
        var otherAdmin = new User(Guid.NewGuid(), $"claim-admin2-{suffix}", $"CLAIM-ADMIN2-{suffix}", "hash", UserRole.Admin);
        var owner = new User(Guid.NewGuid(), $"claim-owner-{suffix}", $"CLAIM-OWNER-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"{suffix[..2]}-{suffix[2..6]}", suffix[..6], "Claim race");
        var stalePreparation = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            owner.Id,
            vehicle.Id,
            actor.Id,
            DateTimeOffset.UtcNow,
            null,
            new EffectiveParkingPolicySnapshot(null, null, true, false));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(actor, otherAdmin, owner);
            seed.Vehicles.Add(vehicle);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            switch (target)
            {
                case "owner":
                    Assert.True(await administration.Service.ArchiveUserAsync(otherAdmin.Id, owner.Id, ct));
                    break;
                case "actor":
                    Assert.True(await administration.Service.SetUserActiveAsync(otherAdmin.Id, actor.Id, false, ct));
                    break;
                case "vehicle":
                    Assert.True(await administration.Service.ArchiveVehicleAsync(otherAdmin.Id, vehicle.Id, ct));
                    break;
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => administration.CapacityClaimer.TryClaimAsync(
                stalePreparation,
                maxGlobalConcurrentVisits: 10,
                maxUserConcurrentVisits: 10,
                ct));

            await using var verify = fixture.CreateDbContext();
            Assert.False(await verify.Visits.AnyAsync(x => x.StartOperationId == stalePreparation.StartOperationId, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents
                .Where(x => x.ActorUserId == actor.Id || x.ActorUserId == otherAdmin.Id || x.ActorUserId == owner.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == actor.Id || x.Id == otherAdmin.Id || x.Id == owner.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Active_visit_claim_serializes_before_archiving()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"serialize-admin-{suffix}", $"SERIALIZE-ADMIN-{suffix}", "hash", UserRole.Admin);
        var owner = new User(Guid.NewGuid(), $"serialize-owner-{suffix}", $"SERIALIZE-OWNER-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"{suffix[..2]}-{suffix[2..6]}", suffix[..6], "Serialize");
        var preparedVisit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            owner.Id,
            vehicle.Id,
            owner.Id,
            DateTimeOffset.UtcNow,
            null,
            new EffectiveParkingPolicySnapshot(null, null, true, false));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, owner);
            seed.Vehicles.Add(vehicle);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            var claim = await administration.CapacityClaimer.TryClaimAsync(preparedVisit, 10, 10, ct);
            Assert.True(claim.Claimed);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.ArchiveUserAsync(admin.Id, owner.Id, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.ArchiveVehicleAsync(admin.Id, vehicle.Id, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(ct, preparedVisit.Id);
            await cleanup.Visits.Where(x => x.Id == preparedVisit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == owner.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Stale_status_updates_cannot_replace_archived_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"concurrency-admin-{suffix}", $"CONCURRENCY-ADMIN-{suffix}", "hash", UserRole.Admin);
        var owner = new User(Guid.NewGuid(), $"concurrency-owner-{suffix}", $"CONCURRENCY-OWNER-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"{suffix[..2]}-{suffix[2..6]}", suffix[..6], "Status concurrency");

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, owner);
            seed.Vehicles.Add(vehicle);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var staleContext = fixture.CreateDbContext();
            var staleOwner = await staleContext.Users.SingleAsync(x => x.Id == owner.Id, ct);
            var staleVehicle = await staleContext.Vehicles.SingleAsync(x => x.Id == vehicle.Id, ct);
            await using var administration = CreateAdministration();
            Assert.True(await administration.Service.ArchiveUserAsync(admin.Id, owner.Id, ct));
            Assert.True(await administration.Service.ArchiveVehicleAsync(admin.Id, vehicle.Id, ct));

            staleOwner.Deactivate();
            staleVehicle.Deactivate();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync(ct));

            await using var verify = fixture.CreateDbContext();
            Assert.Equal(UserStatus.Archived, await verify.Users.Where(x => x.Id == owner.Id).Select(x => x.Status).SingleAsync(ct));
            Assert.Equal(VehicleStatus.Archived, await verify.Vehicles.Where(x => x.Id == vehicle.Id).Select(x => x.Status).SingleAsync(ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == owner.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_deactivated_or_archived()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"only-admin-{suffix}", $"ONLY-ADMIN-{suffix}", "hash", UserRole.Admin);
        Guid[] previouslyActiveAdministratorIds;

        await using (var seed = fixture.CreateDbContext())
        {
            previouslyActiveAdministratorIds = await seed.Users
                .Where(x => x.Role == UserRole.Admin && x.Status == UserStatus.Active)
                .Select(x => x.Id)
                .ToArrayAsync(ct);
            seed.Users.Add(admin);
            await seed.SaveChangesAsync(ct);
            if (previouslyActiveAdministratorIds.Length > 0)
                await seed.Users
                    .Where(x => previouslyActiveAdministratorIds.Contains(x.Id))
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, UserStatus.Inactive), ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.SetUserActiveAsync(admin.Id, admin.Id, false, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => administration.Service.ArchiveUserAsync(admin.Id, admin.Id, ct));

            await using var verify = fixture.CreateDbContext();
            var persisted = await verify.Users.SingleAsync(x => x.Id == admin.Id, ct);
            Assert.Equal(UserStatus.Active, persisted.Status);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            if (previouslyActiveAdministratorIds.Length > 0)
                await cleanup.Users
                    .Where(x => previouslyActiveAdministratorIds.Contains(x.Id))
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, UserStatus.Active), ct);
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
        }
    }

    private static Visit CompletedVisit(
        Guid userId,
        Guid vehicleId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            userId,
            vehicleId,
            userId,
            start,
            end,
            new EffectiveParkingPolicySnapshot(null, null, true, false));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(end);
        return visit;
    }

    private static ProviderParkingAction CompletedProviderAction(
        Guid visitId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var action = new ProviderParkingAction(Guid.NewGuid(), visitId, start, end);
        action.MarkStarting();
        action.MarkActive($"analysis-action-{Guid.NewGuid():N}", start);
        action.MarkCompleted(end);
        return action;
    }

    private async Task ResetAnalysisStateAsync(CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await context.ProviderOperations.ExecuteDeleteAsync(ct);
        await context.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await context.DeleteVisitSchedulerAuditEventsAsync(ct);
        await context.Visits.ExecuteDeleteAsync(ct);
        await context.ParkingTariffs.ExecuteDeleteAsync(ct);
        await context.PaidWindows.ExecuteDeleteAsync(ct);
        await context.ParkingCalendarExceptions.ExecuteDeleteAsync(ct);
        await context.ParkingRuleSets.ExecuteDeleteAsync(ct);

        var paidWindows = Enumerable.Range((int)DayOfWeek.Monday, 6)
            .Select(day => new PaidWindow(
                (DayOfWeek)day,
                new TimeOnly(9, 0),
                new TimeOnly(20, 0)))
            .ToArray();

        context.ParkingRuleSets.Add(new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            validUntil: null,
            TimeSpan.FromHours(4),
            paidWindows,
            publicHolidaysAreFree: true,
            continuation: ProviderCoverageContinuation.StartNewAction));

        context.ParkingTariffs.Add(new ParkingTariff(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            validUntil: null,
            rate: 2m,
            unit: ParkingTariffUnit.Hour));

        await context.SaveChangesAsync(ct);
    }

    private AdministrationScope CreateAdministration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        return new AdministrationScope(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<IAdministrationService>(),
            scope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>());
    }

    private sealed class AdministrationScope(
        ServiceProvider provider,
        AsyncServiceScope scope,
        IAdministrationService service,
        IVisitCapacityClaimer capacityClaimer) : IAsyncDisposable
    {
        public IAdministrationService Service { get; } = service;
        public IVisitCapacityClaimer CapacityClaimer { get; } = capacityClaimer;

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
