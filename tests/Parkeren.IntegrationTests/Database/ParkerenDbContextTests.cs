using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ParkerenDbContextTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Database_can_apply_migrations()
    {
        await using var context = fixture.CreateDbContext();

        Assert.True(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Visit_capacity_claim_allows_only_one_start_for_last_slot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        var user1 = new User(Guid.NewGuid(), "visitor-1", "VISITOR-1", "hash", UserRole.Visitor);
        var user2 = new User(Guid.NewGuid(), "visitor-2", "VISITOR-2", "hash", UserRole.Visitor);
        var vehicle1 = new Vehicle(Guid.NewGuid(), "AA-11-AA", "AA11AA", null);
        var vehicle2 = new Vehicle(Guid.NewGuid(), "BB-22-BB", "BB22BB", null);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.AddRange(user1, user2);
            seedContext.Vehicles.AddRange(vehicle1, vehicle2);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit1 = new Visit(Guid.NewGuid(), Guid.NewGuid(), user1.Id, vehicle1.Id, user1.Id, startAt, startAt.AddHours(1), snapshot);
        var visit2 = new Visit(Guid.NewGuid(), Guid.NewGuid(), user2.Id, vehicle2.Id, user2.Id, startAt, startAt.AddHours(1), snapshot);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        async Task<VisitCapacityClaim> ClaimAsync(Visit visit)
        {
            await using var scope = provider.CreateAsyncScope();
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            return await claimer.TryClaimAsync(visit, 1, cancellationToken);
        }

        var claims = await Task.WhenAll(ClaimAsync(visit1), ClaimAsync(visit2));

        Assert.Single(claims, claim => claim.Claimed);
        Assert.Single(claims, claim => !claim.Claimed);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.Visits.CountAsync(
            visit => visit.Status != VisitStatus.Completed && visit.Status != VisitStatus.Cancelled,
            cancellationToken));
    }
    [Fact]
    public async Task Visit_capacity_claim_replays_same_operation_without_duplicate_visit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        var user = new User(Guid.NewGuid(), "visitor-replay", "VISITOR-REPLAY", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "CC-33-CC", "CC33CC", null);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit1 = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);
        var visit2 = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        async Task<VisitCapacityClaim> ClaimAsync(Visit visit)
        {
            await using var scope = provider.CreateAsyncScope();
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            return await claimer.TryClaimAsync(visit, 5, cancellationToken);
        }

        var claims = await Task.WhenAll(ClaimAsync(visit1), ClaimAsync(visit2));

        Assert.All(claims, claim => Assert.True(claim.Claimed));
        Assert.Single(claims, claim => claim.IsReplay);
        Assert.Single(claims, claim => !claim.IsReplay);
        Assert.Equal(claims[0].Visit!.Id, claims[1].Visit!.Id);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.Visits.CountAsync(
            visit => visit.StartOperationId == operationId,
            cancellationToken));
    }

    private async Task ClearVisitsAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.Visits.ExecuteDeleteAsync(cancellationToken);
    }

}
