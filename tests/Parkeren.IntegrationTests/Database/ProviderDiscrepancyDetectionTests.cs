using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderDiscrepancyDetectionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task External_stop_is_recorded_as_resolved_discrepancy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);
        var remoteProduct = await parkingProvider.GetProductAsync(cancellationToken);
        var localProduct = await GetOrCreateProductAsync(remoteProduct, cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"disc-stop-{suffix}", $"DISC-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"DS-{suffix}", $"DS{suffix}".ToUpperInvariant(), null);
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        var end = start.AddHours(1);
        var remote = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(
                vehicle.NormalizedLicensePlate,
                start,
                end,
                remoteProduct.Location,
                remoteProduct.Id),
            cancellationToken);

        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            start,
            end,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true),
            localProduct.Id,
            remoteProduct.Id,
            remoteProduct.Location);
        visit.Activate();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(),
            visit.Id,
            start,
            end,
            remoteProduct.Id,
            remoteProduct.Location);
        action.MarkStarting();
        action.MarkActive(remote.ProviderActionId, start, remote.Status);

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.Add(user);
                seed.Vehicles.Add(vehicle);
                seed.Visits.Add(visit);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(cancellationToken);
            }

            (await http.PostAsync(
                $"api/test/actions/{remote.ProviderActionId}/stop",
                null,
                cancellationToken)).EnsureSuccessStatusCode();

            await RunProviderCheckAsync(parkingProvider, cancellationToken);

            await using var verify = fixture.CreateDbContext();
            var discrepancy = Assert.Single(await verify.ProviderDiscrepancies.AsNoTracking()
                .Where(x => x.ProviderParkingActionId == action.Id)
                .ToListAsync(cancellationToken));

            Assert.Equal(ProviderDiscrepancyType.ProviderActionStatusMismatch, discrepancy.Type);
            Assert.Equal(ProviderDiscrepancyStatus.Resolved, discrepancy.Status);
            Assert.Equal("stopped", discrepancy.ProviderStatus, ignoreCase: true);
            Assert.NotNull(discrepancy.ResolvedAt);

            var persistedAction = await verify.ProviderParkingActions
                .SingleAsync(x => x.Id == action.Id, cancellationToken);
            Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        }
        finally
        {
            await CleanupVisitAsync(visit.Id, user.Id, vehicle.Id, cancellationToken);
        }
    }

    [Fact]
    public async Task Provider_only_action_is_open_until_provider_reports_it_stopped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);
        var remoteProduct = await parkingProvider.GetProductAsync(cancellationToken);
        var localProduct = await GetOrCreateProductAsync(remoteProduct, cancellationToken);

        var start = DateTimeOffset.UtcNow;
        var remote = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(
                "EXTERNAL1",
                start,
                start.AddHours(1),
                remoteProduct.Location,
                remoteProduct.Id),
            cancellationToken);

        try
        {
            await RunProviderCheckAsync(parkingProvider, cancellationToken);

            await using (var verify = fixture.CreateDbContext())
            {
                var discrepancy = Assert.Single(await verify.ProviderDiscrepancies.AsNoTracking()
                    .Where(x => x.ProviderProductId == localProduct.Id &&
                                x.ProviderActionId == remote.ProviderActionId)
                    .ToListAsync(cancellationToken));
                Assert.Equal(ProviderDiscrepancyType.ExternalProviderAction, discrepancy.Type);
                Assert.Equal(ProviderDiscrepancyStatus.Open, discrepancy.Status);
                Assert.Null(discrepancy.VisitId);
            }

            (await http.PostAsync(
                $"api/test/actions/{remote.ProviderActionId}/stop",
                null,
                cancellationToken)).EnsureSuccessStatusCode();
            await RunProviderCheckAsync(parkingProvider, cancellationToken);

            await using var resolved = fixture.CreateDbContext();
            var persisted = Assert.Single(await resolved.ProviderDiscrepancies.AsNoTracking()
                .Where(x => x.ProviderProductId == localProduct.Id &&
                            x.ProviderActionId == remote.ProviderActionId)
                .ToListAsync(cancellationToken));
            Assert.Equal(ProviderDiscrepancyStatus.Resolved, persisted.Status);
            Assert.NotNull(persisted.ResolvedAt);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderDiscrepancies
                .Where(x => x.ProviderProductId == localProduct.Id &&
                            x.ProviderActionId == remote.ProviderActionId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<ParkingProviderProduct> GetOrCreateProductAsync(
        ProviderProduct remoteProduct,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        var existing = await context.ParkingProviderProducts
            .SingleOrDefaultAsync(
                x => x.ProviderProductId == remoteProduct.Id,
                cancellationToken);
        if (existing is not null)
            return existing;

        var product = new ParkingProviderProduct(
            Guid.NewGuid(),
            remoteProduct.Id,
            remoteProduct.Name,
            remoteProduct.CategoryId,
            remoteProduct.CategoryName,
            remoteProduct.Location,
            DateTimeOffset.UtcNow);
        context.ParkingProviderProducts.Add(product);
        await context.SaveChangesAsync(cancellationToken);
        return product;
    }

    private async Task RunProviderCheckAsync(
        IParkingProvider parkingProvider,
        CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton(parkingProvider);
        services.AddLogging();

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
            .ReconcileActiveProviderActionsAsync(cancellationToken);
    }

    private async Task CleanupVisitAsync(
        Guid visitId,
        Guid userId,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();

        await context.Notifications.Where(x => x.VisitId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.NotificationEvents.Where(x => x.AggregateId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ProviderDiscrepancies.Where(x => x.VisitId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.VisitSchedulerWork.Where(x => x.VisitId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ProviderOperations.Where(x => x.VisitId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.VisitEndTimeChanges.Where(x => x.VisitId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ProviderParkingActions.Where(x => x.VisitId == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Visits.Where(x => x.Id == visitId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Vehicles.Where(x => x.Id == vehicleId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Users.Where(x => x.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
