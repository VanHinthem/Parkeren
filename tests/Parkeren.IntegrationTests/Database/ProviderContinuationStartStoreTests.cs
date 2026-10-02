using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderContinuationStartStoreTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Prepare_attempt_allows_future_predecessor_inside_jit_window()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        var predecessorEnd = now.AddMinutes(4);
        var requestedEnd = predecessorEnd.AddHours(1);
        var user = new User(Guid.NewGuid(), $"jit-{Guid.NewGuid():N}", $"JIT-{Guid.NewGuid():N}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "JT-01-TS", "JT01TS", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddHours(-1),
            requestedEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();

        var predecessor = new ProviderParkingAction(
            Guid.NewGuid(),
            visit.Id,
            now.AddHours(-1),
            predecessorEnd);
        predecessor.MarkStarting();
        predecessor.MarkActive("provider-predecessor", now.AddHours(-1), "active");

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(predecessor);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
            });
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var store = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartStore>();
            var operationId = Guid.NewGuid();
            var preparation = await store.PrepareAttemptAsync(
                visit,
                predecessor,
                operationId,
                requestedEnd,
                cancellationToken);

            Assert.False(preparation.IsReplay);
            Assert.True(preparation.AttemptStartedNow);
            Assert.Equal(ProviderOperationType.ContinueStart, preparation.Operation.Type);
            Assert.Equal(ProviderOperationStatus.InProgress, preparation.Operation.Status);
            Assert.Equal(ProviderActionState.Starting, preparation.Action.State);
            Assert.Equal(predecessorEnd.AddSeconds(1), preparation.Action.PlannedStartAt);
            Assert.Equal(requestedEnd, preparation.Action.PlannedEndAt);

            await using var verifyContext = fixture.CreateDbContext();
            Assert.Equal(2, await verifyContext.ProviderParkingActions.CountAsync(
                x => x.VisitId == visit.Id,
                cancellationToken));
            Assert.Single(await verifyContext.ProviderOperations
                .Where(x => x.VisitId == visit.Id && x.OperationId == operationId)
                .ToListAsync(cancellationToken));
        }
        finally
        {
            await using var cleanupContext = fixture.CreateDbContext();
            await cleanupContext.ProviderOperations
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ProviderParkingActions
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Visits
                .Where(x => x.Id == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Vehicles
                .Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Users
                .Where(x => x.Id == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
