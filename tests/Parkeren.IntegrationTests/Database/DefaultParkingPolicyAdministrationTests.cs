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
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
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
            Assert.Single(result.BlockedFields);
            Assert.Contains(AdminDefaultPolicyField.AllowVisitExtension, result.BlockedFields);

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
        var defaultPolicyCount = await seed.DefaultParkingPolicies.CountAsync(ct);
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
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
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
            var effectivePolicy = await verify.DefaultParkingPolicies
                .OrderByDescending(x => x.UpdatedAt)
                .FirstAsync(ct);
            Assert.False(effectivePolicy.AllowVisitExtension);
            Assert.Equal(defaults.Id, effectivePolicy.Id);
            Assert.Equal(defaultPolicyCount, await verify.DefaultParkingPolicies.CountAsync(ct));
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
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
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
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
            var restoreDefaults = await cleanup.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct);
            Restore(restoreDefaults, original);
            (await cleanup.ParkingSystemSettings.SingleAsync(ct)).SetMaxConcurrentVisits(originalGlobal);
            await cleanup.SaveChangesAsync(ct);
        }
    }

    [Fact]
    public async Task Warning_settings_are_persisted_and_returned_by_admin_read_model()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);

        await using var seed = fixture.CreateDbContext();
        var settings = await seed.ParkingSystemSettings.SingleAsync(ct);
        var originalWarningAfter = settings.LongVisitWarningAfter;
        var originalNotifyAdmin = settings.NotifyAdminOnLongVisit;
        var originalReminder = settings.LongVisitReminderInterval;
        var originalThresholds = settings.BudgetWarningThresholdPercentages.ToArray();
        seed.Users.Add(admin);
        await seed.SaveChangesAsync(ct);

        try
        {
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

            var update = await administration.SetWarningSettingsAsync(
                admin.Id,
                longVisitWarningAfterMinutes: 360,
                notifyAdminOnLongVisit: false,
                longVisitReminderIntervalMinutes: 120,
                budgetWarningThresholdPercentages: new[] { 75, 90, 100 },
                ct);

            Assert.Equal(AdminWarningSettingsUpdateOutcome.Updated, update.Outcome);

            var read = await administration.GetSystemSettingsAsync(admin.Id, ct);
            Assert.Equal(360, read.LongVisitWarningAfterMinutes);
            Assert.False(read.NotifyAdminOnLongVisit);
            Assert.Equal(120, read.LongVisitReminderIntervalMinutes);
            Assert.Equal(new[] { 75, 90, 100 }, read.BudgetWarningThresholdPercentages);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
            var restore = await cleanup.ParkingSystemSettings.SingleAsync(ct);
            restore.SetLongVisitNotifications(originalWarningAfter, originalNotifyAdmin, originalReminder);
            restore.SetBudgetWarningThresholdPercentages(originalThresholds);
            await cleanup.SaveChangesAsync(ct);
        }
    }

    [Fact]
    public async Task Lowering_global_capacity_clamps_default_concurrency()
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
        settings.SetMaxConcurrentVisits(5);
        defaults.SetValues(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false, 5);
        seed.Users.Add(admin);
        await seed.SaveChangesAsync(ct);

        try
        {
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

            Assert.True(await administration.SetGlobalMaxConcurrentVisitsAsync(admin.Id, 3, ct));

            await using var verify = fixture.CreateDbContext();
            Assert.Equal(3, (await verify.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct)).MaxConcurrentVisits);
            Assert.Equal(3, (await verify.ParkingSystemSettings.SingleAsync(ct)).MaxConcurrentVisits);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.AdminAuditEvents.Where(x => x.ActorUserId == admin.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
            var restoreDefaults = await cleanup.DefaultParkingPolicies.OrderByDescending(x => x.UpdatedAt).FirstAsync(ct);
            Restore(restoreDefaults, original);
            (await cleanup.ParkingSystemSettings.SingleAsync(ct)).SetMaxConcurrentVisits(originalGlobal);
            await cleanup.SaveChangesAsync(ct);
        }
    }

    private ServiceCollection CreateServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
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
        await cleanup.AdminAuditEvents
            .Where(x => x.ActorUserId == adminId || x.ActorUserId == visitorId)
            .ExecuteDeleteAsync(cancellationToken);
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
        await context.DeleteVisitSchedulerAuditEventsAsync(cancellationToken);
        await context.Notifications.ExecuteDeleteAsync(cancellationToken);
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
