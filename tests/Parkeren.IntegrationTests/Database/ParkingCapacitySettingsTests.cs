using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ParkingCapacitySettingsTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task New_admin_gets_explicit_global_capacity_override()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorName = $"admin-{Guid.NewGuid():N}";
        var actor = new User(Guid.NewGuid(), actorName, actorName.ToUpperInvariant(), "hash", UserRole.Admin);
        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(actor);
            await seedContext.SaveChangesAsync(ct);
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
        var adminName = $"new-admin-{Guid.NewGuid():N}";
        var visitorName = $"new-visitor-{Guid.NewGuid():N}";
        var admin = await administration.CreateUserAsync(actor.Id, adminName, "123456", UserRole.Admin, ct);
        var visitor = await administration.CreateUserAsync(actor.Id, visitorName, "123456", UserRole.Visitor, ct);

        Assert.NotNull(admin);
        Assert.NotNull(visitor);
        await using var context = fixture.CreateDbContext();
        Assert.Equal<int?>(5, await context.UserPolicyOverrides
            .Where(x => x.UserId == admin.Id).Select(x => x.MaxConcurrentVisits).SingleAsync(ct));
        Assert.False(await context.UserPolicyOverrides.AnyAsync(x => x.UserId == visitor.Id, ct));
    }

    [Fact]
    public async Task Lowering_global_limit_clamps_overrides_but_raising_it_does_not()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var adminName = $"admin-{Guid.NewGuid():N}";
        var visitorName = $"visitor-{Guid.NewGuid():N}";
        var admin = new User(Guid.NewGuid(), adminName, adminName.ToUpperInvariant(), "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), visitorName, visitorName.ToUpperInvariant(), "hash", UserRole.Visitor);
        await using var context = fixture.CreateDbContext();
        context.Users.AddRange(admin, visitor);
        await context.SaveChangesAsync(ct);
        var settings = await context.ParkingSystemSettings.SingleAsync(ct);
        settings.SetMaxConcurrentVisits(5);
        await context.SaveChangesAsync(ct);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

        try
        {
            Assert.True(await administration.SetUserMaxConcurrentVisitsAsync(admin.Id, visitor.Id, 5, ct));
            Assert.True(await administration.SetGlobalMaxConcurrentVisitsAsync(admin.Id, 3, ct));
            Assert.Equal(3, await administration.GetGlobalMaxConcurrentVisitsAsync(admin.Id, ct));
            Assert.Equal<int?>(3, await context.UserPolicyOverrides.Where(x => x.UserId == visitor.Id)
                .Select(x => x.MaxConcurrentVisits).SingleAsync(ct));

            Assert.True(await administration.SetGlobalMaxConcurrentVisitsAsync(admin.Id, 5, ct));
            Assert.Equal<int?>(3, await context.UserPolicyOverrides.Where(x => x.UserId == visitor.Id)
                .Select(x => x.MaxConcurrentVisits).SingleAsync(ct));
            Assert.False(await administration.SetUserMaxConcurrentVisitsAsync(admin.Id, visitor.Id, 6, ct));
            Assert.True(await administration.SetUserMaxConcurrentVisitsAsync(admin.Id, visitor.Id, null, ct));
            Assert.False(await context.UserPolicyOverrides.AnyAsync(x => x.UserId == visitor.Id, ct));
        }
        finally
        {
            await using var restore = fixture.CreateDbContext();
            (await restore.ParkingSystemSettings.SingleAsync(ct)).SetMaxConcurrentVisits(5);
            await restore.SaveChangesAsync(ct);
        }
    }

    [Fact]
    public async Task Active_visit_blocks_global_change_and_claim_uses_persisted_capacity()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var adminName = $"admin-{Guid.NewGuid():N}";
        var visitorName = $"visitor-{Guid.NewGuid():N}";
        var plate = $"PL{Guid.NewGuid():N}"[..8];
        var admin = new User(Guid.NewGuid(), adminName, adminName.ToUpperInvariant(), "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), visitorName, visitorName.ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        await using (var context = fixture.CreateDbContext())
        {
            context.Users.AddRange(admin, visitor);
            context.Vehicles.Add(vehicle);
            (await context.ParkingSystemSettings.SingleAsync(ct)).SetMaxConcurrentVisits(1);
            await context.SaveChangesAsync(ct);
        }

        try
        {
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            var startAt = DateTimeOffset.UtcNow;
            var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
            var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), visitor.Id, vehicle.Id, visitor.Id, startAt, startAt.AddHours(1), snapshot);

            Assert.True((await claimer.TryClaimAsync(visit, 5, 5, ct)).Claimed);
            Assert.False(await administration.SetGlobalMaxConcurrentVisitsAsync(admin.Id, 2, ct));
            Assert.Equal(1, await administration.GetGlobalMaxConcurrentVisitsAsync(admin.Id, ct));

            var secondVisit = new Visit(Guid.NewGuid(), Guid.NewGuid(), visitor.Id, vehicle.Id, visitor.Id, startAt, startAt.AddHours(1), snapshot);
            Assert.False((await claimer.TryClaimAsync(secondVisit, 5, 5, ct)).Claimed);
        }
        finally
        {
            await ClearVisitsAsync(ct);
            await using var context = fixture.CreateDbContext();
            (await context.ParkingSystemSettings.SingleAsync(ct)).SetMaxConcurrentVisits(5);
            await context.SaveChangesAsync(ct);
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

    private async Task ClearVisitsAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(cancellationToken);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderOperations.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderParkingActions.ExecuteDeleteAsync(cancellationToken);
        await context.DeleteVisitSchedulerAuditEventsAsync(cancellationToken);
        await context.Visits.ExecuteDeleteAsync(cancellationToken);
    }
}
