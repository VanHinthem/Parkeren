using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class DefaultParkingPolicyAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Default_change_is_blocked_for_active_visit_that_inherits_changed_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"visitor-{suffix}", $"VISITOR-{suffix}", "hash", UserRole.Visitor);
        var plate = $"DF{suffix}"[..8];
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);

        await using var seed = fixture.CreateDbContext();
        var defaults = await seed.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct);
        var original = Snapshot(defaults);
        defaults.SetValues(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false, 1);

        seed.Users.AddRange(admin, visitor);
        seed.Vehicles.Add(vehicle);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            visitor.Id,
            vehicle.Id,
            visitor.Id,
            now,
            now.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false));
        visit.Activate();
        seed.Visits.Add(visit);
        await seed.SaveChangesAsync(ct);

        try
        {
            var administration = CreateAdministration();
            var result = await administration.SetDefaultParkingPolicyAsync(
                admin.Id,
                240,
                480,
                allowVisitExtension: false,
                allowOpenEndedVisits: false,
                maxConcurrentVisits: 1,
                ct);

            Assert.Equal(AdminDefaultPolicyUpdateOutcome.ActiveVisitConflict, result.Outcome);
            Assert.Equal(1, result.AffectedActiveVisitCount);
            Assert.Equal(new[] { AdminDefaultPolicyField.AllowVisitExtension }, result.BlockedFields);

            await using var verify = fixture.CreateDbContext();
            Assert.True((await verify.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct)).AllowVisitExtension);
        }
        finally
        {
            await CleanupAsync(admin.Id, visitor.Id, vehicle.Id, original, ct);
        }
    }

    [Fact]
    public async Task Default_change_is_allowed_when_active_user_overrides_changed_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"visitor-{suffix}", $"VISITOR-{suffix}", "hash", UserRole.Visitor);
        var plate = $"OV{suffix}"[..8];
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);

        await using var seed = fixture.CreateDbContext();
        var defaults = await seed.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct);
        var original = Snapshot(defaults);
        defaults.SetValues(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false, 1);

        var policyOverride = new UserPolicyOverride(visitor.Id);
        policyOverride.SetOverrides(
            PolicyDurationOverrideMode.Inherit,
            null,
            PolicyDurationOverrideMode.Inherit,
            null,
            allowVisitExtension: true,
            allowOpenEndedVisits: null,
            maxConcurrentVisits: null);

        seed.Users.AddRange(admin, visitor);
        seed.Vehicles.Add(vehicle);
        seed.UserPolicyOverrides.Add(policyOverride);

        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            visitor.Id,
            vehicle.Id,
            visitor.Id,
            now,
            now.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false));
        visit.Activate();
        seed.Visits.Add(visit);
        await seed.SaveChangesAsync(ct);

        try
        {
            var administration = CreateAdministration();
            var result = await administration.SetDefaultParkingPolicyAsync(
                admin.Id,
                240,
                480,
                allowVisitExtension: false,
                allowOpenEndedVisits: false,
                maxConcurrentVisits: 1,
                ct);

            Assert.Equal(AdminDefaultPolicyUpdateOutcome.Updated, result.Outcome);
            Assert.Equal(0, result.AffectedActiveVisitCount);
            Assert.Empty(result.BlockedFields);

            await using var verify = fixture.CreateDbContext();
            Assert.False((await verify.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct)).AllowVisitExtension);
        }
        finally
        {
            await CleanupAsync(admin.Id, visitor.Id, vehicle.Id, original, ct);
        }
    }

    [Fact]
    public async Task Default_concurrency_cannot_exceed_global_capacity()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);

        await using var seed = fixture.CreateDbContext();
        var defaults = await seed.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct);
        var original = Snapshot(defaults);
        var settings = await seed.ParkingSystemSettings.SingleAsync(ct);
        var originalGlobal = settings.MaxConcurrentVisits;
        settings.SetMaxConcurrentVisits(3);
        defaults.SetValues(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false, 1);
        seed.Users.Add(admin);
        await seed.SaveChangesAsync(ct);

        try
        {
            var administration = CreateAdministration();
            var result = await administration.SetDefaultParkingPolicyAsync(
                admin.Id,
                240,
                480,
                allowVisitExtension: true,
                allowOpenEndedVisits: false,
                maxConcurrentVisits: 4,
                ct);

            Assert.Equal(AdminDefaultPolicyUpdateOutcome.Invalid, result.Outcome);
            Assert.Equal(1, result.CurrentPolicy.MaxConcurrentVisits);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
            var restoreDefaults = await cleanup.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct);
            Restore(restoreDefaults, original);
            (await cleanup.ParkingSystemSettings.SingleAsync(ct)).SetMaxConcurrentVisits(originalGlobal);
            await cleanup.SaveChangesAsync(ct);
        }
    }

    private IAdministrationService CreateAdministration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAdministrationService>();
    }

    private async Task CleanupAsync(
        Guid adminId,
        Guid visitorId,
        Guid vehicleId,
        DefaultPolicyState original,
        CancellationToken cancellationToken)
    {
        await ClearVisitsAsync(cancellationToken);
        await using var cleanup = fixture.CreateDbContext();
        await cleanup.UserPolicyOverrides.Where(x => x.UserId == visitorId || x.UserId == adminId).ExecuteDeleteAsync(cancellationToken);
        await cleanup.Users.Where(x => x.Id == adminId || x.Id == visitorId).ExecuteDeleteAsync(cancellationToken);
        await cleanup.Vehicles.Where(x => x.Id == vehicleId).ExecuteDeleteAsync(cancellationToken);
        var defaults = await cleanup.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(cancellationToken);
        Restore(defaults, original);
        await cleanup.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearVisitsAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(cancellationToken);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderOperations.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderParkingActions.ExecuteDeleteAsync(cancellationToken);
        await context.Visits.ExecuteDeleteAsync(cancellationToken);
    }

    private static DefaultPolicyState Snapshot(DefaultParkingPolicy policy) =>
        new(
            policy.MaxPaidParkingDuration,
            policy.MaxVisitElapsedDuration,
            policy.AllowVisitExtension,
            policy.AllowOpenEndedVisits,
            policy.MaxConcurrentVisits);

    private static void Restore(DefaultParkingPolicy policy, DefaultPolicyState state) =>
        policy.SetValues(
            state.MaxPaidParkingDuration,
            state.MaxVisitElapsedDuration,
            state.AllowVisitExtension,
            state.AllowOpenEndedVisits,
            state.MaxConcurrentVisits);

    private sealed record DefaultPolicyState(
        TimeSpan? MaxPaidParkingDuration,
        TimeSpan? MaxVisitElapsedDuration,
        bool AllowVisitExtension,
        bool AllowOpenEndedVisits,
        int MaxConcurrentVisits);
}
