using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Notifications;
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



    [Fact]
    public async Task Stop_visit_context_is_resolved_from_persisted_actor_and_visit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"stop-resolver-{suffix}", $"STOP-RESOLVER-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SR-{suffix[..2]}-{suffix[2..4]}", $"SR{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now, now.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IStopVisitRequestResolver>();
        var resolved = await resolver.ResolveAsync(user.Id, visit.Id, cancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(user.Id, resolved.Actor.Id);
        Assert.Equal(UserRole.Visitor, resolved.Actor.Role);
        Assert.True(resolved.Actor.IsActive);
        Assert.Equal(visit.Id, resolved.Visit.Id);
        Assert.Equal(user.Id, resolved.Visit.UserId);
    }

    [Fact]
    public async Task Visit_stop_claim_serializes_same_operation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "stop-race", "STOP-RACE", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "ST-11-OP", "ST11OP", null);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var operationId = Guid.NewGuid();
        var command = new StopVisitCommand(operationId, visit.Id, user.Id);

        async Task<StopVisitClaim> ClaimAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            var claimer = scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            return await claimer.ClaimAsync(command, cancellationToken);
        }

        var claims = await Task.WhenAll(ClaimAsync(), ClaimAsync());

        Assert.Single(claims, claim => !claim.IsReplay);
        Assert.Single(claims, claim => claim.IsReplay);
        Assert.All(claims, claim => Assert.Equal(VisitStatus.Stopping, claim.Visit.Status));
        Assert.Equal(claims[0].Operation!.Id, claims[1].Operation!.Id);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var operations = await verifyContext.ProviderOperations
            .Where(x => x.OperationId == operationId)
            .ToListAsync(cancellationToken);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Single(operations);
        Assert.Equal(ProviderOperationType.Stop, operations[0].Type);
        Assert.Equal(ProviderOperationStatus.Pending, operations[0].Status);
        Assert.Equal(visit.Id, operations[0].VisitId);
    }



    [Fact]
    public async Task Provider_free_stop_completes_visit_and_releases_capacity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "free-stop", "FREE-STOP", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "FR-33-EE", "FR33EE", null);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var operationId = Guid.NewGuid();
        StopVisitClaim claim;
        await using (var claimScope = provider.CreateAsyncScope())
        {
            var claimer = claimScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            claim = await claimer.ClaimAsync(
                new StopVisitCommand(operationId, visit.Id, user.Id),
                cancellationToken);
        }

        var actualEndAt = DateTimeOffset.UtcNow;
        await using (var finalizeScope = provider.CreateAsyncScope())
        {
            var finalizer = finalizeScope.ServiceProvider.GetRequiredService<IStopVisitFinalizer>();
            await finalizer.CompleteWithoutProviderActionAsync(claim, actualEndAt, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var operation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);

        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.Equal(VisitHealth.Healthy, persistedVisit.Health);
        Assert.NotNull(persistedVisit.ActualEndAt);
        Assert.True((persistedVisit.ActualEndAt.Value - actualEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.False(persistedVisit.OccupiesCapacity);
        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.NotNull(operation.CompletedAt);
        Assert.False(await verifyContext.ProviderParkingActions.AnyAsync(x => x.VisitId == visit.Id, cancellationToken));
        Assert.Single(await verifyContext.NotificationEvents
            .Where(x => x.Type == NotificationEventType.VisitStopped && x.AggregateId == visit.Id)
            .ToListAsync(cancellationToken));
    }


    [Fact]
    public async Task Provider_start_prepare_serializes_same_operation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "provider-race", "PROVIDER-RACE", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "DD-44-DD", "DD44DD", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var claim = new StartVisitClaimResult(visit, false, true);
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        async Task<ProviderStartPreparation> PrepareAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            return await store.PrepareAttemptAsync(claim, startAt.AddHours(1), cancellationToken);
        }

        var preparations = await Task.WhenAll(PrepareAsync(), PrepareAsync());

        Assert.Single(preparations, preparation => !preparation.IsReplay && preparation.AttemptStartedNow);
        Assert.Single(preparations, preparation => preparation.IsReplay && !preparation.AttemptStartedNow);
        Assert.Equal(preparations[0].Operation.Id, preparations[1].Operation.Id);
        Assert.Equal(preparations[0].Action.Id, preparations[1].Action.Id);

        await using var verifyContext = fixture.CreateDbContext();
        var operation = await verifyContext.ProviderOperations.SingleAsync(
            candidate => candidate.OperationId == operationId,
            cancellationToken);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Equal(ProviderOperationStatus.InProgress, operation.Status);
        Assert.Equal(1, await verifyContext.ProviderParkingActions.CountAsync(
            action => action.VisitId == visit.Id,
            cancellationToken));
    }


    [Fact]
    public async Task Provider_start_prepare_recovers_persisted_in_progress_operation_after_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "provider-restart", "PROVIDER-RESTART", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "EE-55-EE", "EE55EE", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var claim = new StartVisitClaimResult(visit, false, true);
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        ProviderStartPreparation first;
        await using (var firstScope = provider.CreateAsyncScope())
        {
            var store = firstScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            first = await store.PrepareAttemptAsync(claim, startAt.AddHours(1), cancellationToken);
        }

        ProviderStartPreparation replay;
        await using (var restartedScope = provider.CreateAsyncScope())
        {
            var store = restartedScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            replay = await store.PrepareAttemptAsync(claim, startAt.AddHours(1), cancellationToken);
        }

        Assert.True(first.AttemptStartedNow);
        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.False(replay.AttemptStartedNow);
        Assert.Equal(first.Operation.Id, replay.Operation.Id);
        Assert.Equal(first.Action.Id, replay.Action.Id);
        Assert.Equal(ProviderOperationStatus.InProgress, replay.Operation.Status);
        Assert.Equal(ProviderActionState.Starting, replay.Action.State);
        Assert.Equal(1, replay.Operation.AttemptCount);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.ProviderOperations.CountAsync(
            candidate => candidate.OperationId == operationId,
            cancellationToken));
        Assert.Equal(1, await verifyContext.ProviderParkingActions.CountAsync(
            action => action.VisitId == visit.Id,
            cancellationToken));
    }


    [Fact]
    public async Task Unknown_provider_start_reconciles_persisted_action_after_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/unknown-outcome", new { StatusCode = 504, Count = 1 }, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var user = new User(Guid.NewGuid(), "provider-unknown", "PROVIDER-UNKNOWN", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "FF-66-FF", "FF66FF", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var endAt = startAt.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();

        var claim = new StartVisitClaimResult(visit, false, true);
        await using (var firstScope = provider.CreateAsyncScope())
        {
            var store = firstScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var executor = firstScope.ServiceProvider.GetRequiredService<StartVisitProviderExecutor>();
            var preparation = await store.PrepareAttemptAsync(claim, endAt, cancellationToken);
            var execution = await executor.ExecuteAsync(
                preparation,
                new ProviderStartRequest("FF66FF", "Oss", endAt),
                cancellationToken);

            Assert.True(execution.RequiresReconciliation);
        }

        await using (var persistedContext = fixture.CreateDbContext())
        {
            var operation = await persistedContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
            var action = await persistedContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
            var persistedVisit = await persistedContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
            Assert.Equal(ProviderOperationStatus.Unknown, operation.Status);
            Assert.Equal(ProviderActionHealth.Unknown, action.Health);
            Assert.Null(action.ProviderActionId);
            Assert.Equal(VisitHealth.Reconciling, persistedVisit.Health);
        }

        await using (var restartedScope = provider.CreateAsyncScope())
        {
            var store = restartedScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var executor = restartedScope.ServiceProvider.GetRequiredService<StartVisitProviderExecutor>();
            var replay = await store.PrepareAttemptAsync(new StartVisitClaimResult(visit, true, true), endAt, cancellationToken);
            var execution = await executor.ExecuteAsync(
                replay,
                new ProviderStartRequest("FF66FF", "Oss", endAt),
                cancellationToken);

            Assert.False(execution.RequiresReconciliation);
            Assert.NotNull(execution.ProviderAction);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var confirmedOperation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var confirmedAction = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        var activeVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.Equal(ProviderOperationStatus.Succeeded, confirmedOperation.Status);
        Assert.Equal(1, confirmedOperation.AttemptCount);
        Assert.Equal(ProviderActionState.Active, confirmedAction.State);
        Assert.Equal(ProviderActionHealth.Healthy, confirmedAction.Health);
        Assert.NotNull(confirmedAction.ProviderActionId);
        Assert.Equal(VisitStatus.Active, activeVisit.Status);
        Assert.Equal(VisitHealth.Healthy, activeVisit.Health);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken), x => x.LicensePlate == "FF66FF");
    }

    [Fact]
    public async Task Late_provider_start_success_preserves_concurrent_stop_claim()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"late-start-{suffix}", $"LATE-START-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"LS-{suffix[..2]}-{suffix[2..4]}", $"LS{suffix[..4]}", null);
        var startOperationId = Guid.NewGuid();
        var stopOperationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var endAt = now.AddHours(1);
        var visit = new Visit(
            Guid.NewGuid(), startOperationId, user.Id, vehicle.Id, user.Id,
            now, endAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using var startScope = provider.CreateAsyncScope();
        var startStore = startScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
        var startResultStore = startScope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
        var preparation = await startStore.PrepareAttemptAsync(
            new StartVisitClaimResult(visit, false, true),
            endAt,
            cancellationToken);

        await using (var stopScope = provider.CreateAsyncScope())
        {
            var stopClaimer = stopScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            await stopClaimer.ClaimAsync(
                new StopVisitCommand(stopOperationId, visit.Id, user.Id),
                cancellationToken);
        }

        var providerAction = new Parkeren.Application.ParkingProvider.ProviderParkingAction(
            $"provider-{suffix}",
            vehicle.NormalizedLicensePlate,
            now,
            endAt,
            "Oss",
            "active");

        await startResultStore.RecordConfirmedAsync(preparation, providerAction, cancellationToken);

        await using (var resumeStopScope = provider.CreateAsyncScope())
        {
            var stopClaimer = resumeStopScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            var stopStore = resumeStopScope.ServiceProvider.GetRequiredService<IProviderStopStore>();
            var replay = await stopClaimer.ClaimAsync(
                new StopVisitCommand(stopOperationId, visit.Id, user.Id),
                cancellationToken);
            var stopPreparation = await stopStore.PrepareAttemptAsync(replay, cancellationToken);

            Assert.True(stopPreparation.AttemptStartedNow);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var persistedAction = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        var persistedStartOperation = await verifyContext.ProviderOperations.SingleAsync(
            x => x.OperationId == startOperationId,
            cancellationToken);
        var persistedStopOperation = await verifyContext.ProviderOperations.SingleAsync(
            x => x.OperationId == stopOperationId,
            cancellationToken);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Equal(ProviderActionState.Stopping, persistedAction.State);
        Assert.Equal(ProviderOperationStatus.Succeeded, persistedStartOperation.Status);
        Assert.Equal(ProviderOperationStatus.InProgress, persistedStopOperation.Status);
        Assert.Equal(persistedAction.Id, persistedStopOperation.ProviderParkingActionId);
    }

    [Fact]
    public async Task Persisted_provider_response_reconciles_after_restart_without_second_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var user = new User(Guid.NewGuid(), "provider-response-restart", "PROVIDER-RESPONSE-RESTART", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "GG-77-GG", "GG77GG", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var endAt = startAt.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();

        var claim = new StartVisitClaimResult(visit, false, true);
        await using (var firstScope = provider.CreateAsyncScope())
        {
            var store = firstScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var resultStore = firstScope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            var preparation = await store.PrepareAttemptAsync(claim, endAt, cancellationToken);

            var providerAction = await parkingProvider.StartActionAsync(
                new ProviderParkingActionRequest("GG77GG", startAt, endAt, "Oss"),
                cancellationToken);
            await resultStore.RecordResponseAsync(preparation, providerAction, cancellationToken);
        }

        await using (var staleContext = fixture.CreateDbContext())
        {
            await staleContext.ProviderOperations
                .Where(x => x.OperationId == operationId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        x => x.AttemptStartedAt,
                        DateTimeOffset.UtcNow.AddMinutes(-6)),
                    cancellationToken);
        }

        await using (var restartedScope = provider.CreateAsyncScope())
        {
            var store = restartedScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var executor = restartedScope.ServiceProvider.GetRequiredService<StartVisitProviderExecutor>();
            var replay = await store.PrepareAttemptAsync(new StartVisitClaimResult(visit, true, true), endAt, cancellationToken);
            var execution = await executor.ExecuteAsync(
                replay,
                new ProviderStartRequest("GG77GG", "Oss", endAt),
                cancellationToken);

            Assert.False(execution.RequiresReconciliation);
            Assert.NotNull(execution.ProviderAction);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var operation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var action = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Equal(ProviderActionState.Active, action.State);
        Assert.Equal(ProviderActionHealth.Healthy, action.Health);
        Assert.NotNull(action.ProviderActionId);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken), x => x.LicensePlate == "GG77GG");
    }

    [Fact]
    public async Task Provider_confirmation_persists_operation_action_and_visit_atomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "provider-confirmed", "PROVIDER-CONFIRMED", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "HH-88-HH", "HH88HH", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var endAt = startAt.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var startStore = scope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var resultStore = scope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            var preparation = await startStore.PrepareAttemptAsync(
                new StartVisitClaimResult(visit, false, true),
                endAt,
                cancellationToken);

            await resultStore.RecordConfirmedAsync(
                preparation,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    "provider-confirmed-1", "HH88HH", startAt, endAt, "Oss", "active"),
                cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedOperation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        var persistedAction = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);

        Assert.Equal(ProviderOperationStatus.Succeeded, persistedOperation.Status);
        Assert.Equal(ProviderActionState.Active, persistedAction.State);
        Assert.Equal(ProviderActionHealth.Healthy, persistedAction.Health);
        Assert.Equal(VisitStatus.Active, persistedVisit.Status);
        Assert.Equal(VisitHealth.Healthy, persistedVisit.Health);
    }

    [Fact]
    public async Task Active_visit_notification_recovers_after_restart_idempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "notification-restart", "NOTIFICATION-RESTART", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "JJ-99-JJ", "JJ99JJ", null);
        var startAt = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using (var restartedScope = provider.CreateAsyncScope())
        {
            var publisher = restartedScope.ServiceProvider.GetRequiredService<IStartVisitNotificationPublisher>();
            await publisher.PublishStartedAsync(visit, cancellationToken);
        }

        await using (var replayScope = provider.CreateAsyncScope())
        {
            var publisher = replayScope.ServiceProvider.GetRequiredService<IStartVisitNotificationPublisher>();
            await publisher.PublishStartedAsync(visit, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.NotificationEvents.CountAsync(
            x => x.Type == NotificationEventType.VisitStarted && x.AggregateId == visit.Id,
            cancellationToken));
    }

    [Fact]
    public async Task Retry_after_visit_commit_before_provider_prepare_creates_single_provider_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "handoff-restart", "HANDOFF-RESTART", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "KK-11-KK", "KK11KK", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var endAt = startAt.AddHours(1);
        var proposedVisit = new Visit(Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        Visit persistedVisit;
        await using (var claimScope = provider.CreateAsyncScope())
        {
            var capacity = claimScope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            var claim = await capacity.TryClaimAsync(proposedVisit, 5, cancellationToken);
            Assert.True(claim.Claimed);
            Assert.False(claim.IsReplay);
            persistedVisit = claim.Visit!;
        }

        // Simulate a process crash after the Visit transaction committed but before
        // any provider operation was created. A retry must reuse the persisted Visit.
        await using (var replayClaimScope = provider.CreateAsyncScope())
        {
            var capacity = replayClaimScope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            var duplicateProposal = new Visit(
                Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);
            var replay = await capacity.TryClaimAsync(duplicateProposal, 5, cancellationToken);

            Assert.True(replay.Claimed);
            Assert.True(replay.IsReplay);
            Assert.Equal(persistedVisit.Id, replay.Visit!.Id);
            persistedVisit = replay.Visit;
        }

        await using (var firstProviderScope = provider.CreateAsyncScope())
        {
            var store = firstProviderScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var preparation = await store.PrepareAttemptAsync(
                new StartVisitClaimResult(persistedVisit, true, true), endAt, cancellationToken);
            Assert.True(preparation.AttemptStartedNow);
        }

        await using (var retryProviderScope = provider.CreateAsyncScope())
        {
            var store = retryProviderScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var replay = await store.PrepareAttemptAsync(
                new StartVisitClaimResult(persistedVisit, true, true), endAt, cancellationToken);
            Assert.True(replay.IsReplay);
            Assert.False(replay.AttemptStartedNow);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.Visits.CountAsync(x => x.StartOperationId == operationId, cancellationToken));
        Assert.Equal(1, await verifyContext.ProviderOperations.CountAsync(x => x.OperationId == operationId, cancellationToken));
        Assert.Equal(1, await verifyContext.ProviderParkingActions.CountAsync(x => x.VisitId == persistedVisit.Id, cancellationToken));
        var operation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        Assert.Equal(1, operation.AttemptCount);
    }

    [Fact]
    public async Task Failed_visit_insert_rolls_back_claim_and_same_operation_can_retry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "rollback-retry", "ROLLBACK-RETRY", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "LL-22-LL", "LL22LL", null);
        var operationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            await seedContext.SaveChangesAsync(cancellationToken);

            var failureConstraintSql =
                """ALTER TABLE visits ADD CONSTRAINT fail_visit_insert_for_test CHECK ("StartOperationId" <> '""" +
                operationId.ToString("D") +
                """');""";
            await seedContext.Database.ExecuteSqlRawAsync(failureConstraintSql, cancellationToken);
        }

        try
        {
            var failedConfiguration = new ConfigurationManager();
            failedConfiguration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
            });
            await using var failedProvider = new ServiceCollection()
                .AddInfrastructure(failedConfiguration)
                .BuildServiceProvider();
            await using var failedScope = failedProvider.CreateAsyncScope();

            var capacity = failedScope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            var visit = new Visit(
                Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id,
                startAt, startAt.AddHours(1), snapshot);

            await Assert.ThrowsAnyAsync<Exception>(() => capacity.TryClaimAsync(visit, 1, cancellationToken));
        }
        finally
        {
            await using var cleanupContext = fixture.CreateDbContext();
            await cleanupContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE visits DROP CONSTRAINT IF EXISTS fail_visit_insert_for_test;",
                cancellationToken);
        }

        await using (var afterFailureContext = fixture.CreateDbContext())
        {
            Assert.Equal(0, await afterFailureContext.Visits.CountAsync(
                x => x.StartOperationId == operationId,
                cancellationToken));
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using (var retryScope = provider.CreateAsyncScope())
        {
            var capacity = retryScope.ServiceProvider.GetRequiredService<IVisitCapacityClaimer>();
            var retryVisit = new Visit(
                Guid.NewGuid(), operationId, user.Id, vehicle.Id, user.Id,
                startAt, startAt.AddHours(1), snapshot);
            var retry = await capacity.TryClaimAsync(retryVisit, 1, cancellationToken);

            Assert.True(retry.Claimed);
            Assert.False(retry.IsReplay);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.Visits.CountAsync(
            x => x.StartOperationId == operationId,
            cancellationToken));
    }


    [Fact]
    public async Task Confirmed_provider_stop_completes_visit_and_releases_capacity_atomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "paid-stop", "PAID-STOP", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "PS-44-OP", "PS44OP", null);
        var startOperationId = Guid.NewGuid();
        var stopOperationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var endAt = startAt.AddHours(2);
        var visit = new Visit(Guid.NewGuid(), startOperationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using (var startScope = provider.CreateAsyncScope())
        {
            var startStore = startScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var startResultStore = startScope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            var preparation = await startStore.PrepareAttemptAsync(
                new StartVisitClaimResult(visit, false, true), endAt, cancellationToken);
            await startResultStore.RecordConfirmedAsync(
                preparation,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    "provider-paid-stop-1", "PS44OP", startAt, endAt, "Oss", "active"),
                cancellationToken);
        }

        StopVisitClaim stopClaim;
        await using (var claimScope = provider.CreateAsyncScope())
        {
            var claimer = claimScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            stopClaim = await claimer.ClaimAsync(
                new StopVisitCommand(stopOperationId, visit.Id, user.Id),
                cancellationToken);
        }

        ProviderStopPreparation stopPreparation;
        await using (var prepareScope = provider.CreateAsyncScope())
        {
            var stopStore = prepareScope.ServiceProvider.GetRequiredService<IProviderStopStore>();
            stopPreparation = await stopStore.PrepareAttemptAsync(stopClaim, cancellationToken);
        }

        var actualEndAt = DateTimeOffset.UtcNow;
        await using (var resultScope = provider.CreateAsyncScope())
        {
            var resultStore = resultScope.ServiceProvider.GetRequiredService<IProviderStopResultStore>();
            await resultStore.RecordConfirmedAsync(
                stopPreparation,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    "provider-paid-stop-1", "PS44OP", startAt, endAt, "Oss", "stopped"),
                actualEndAt,
                cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var action = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        var stopOperation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == stopOperationId, cancellationToken);

        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.Equal(VisitHealth.Healthy, persistedVisit.Health);
        Assert.False(persistedVisit.OccupiesCapacity);
        Assert.Equal(ProviderActionState.Stopped, action.State);
        Assert.Equal(ProviderActionHealth.Healthy, action.Health);
        Assert.Equal(ProviderOperationStatus.Succeeded, stopOperation.Status);
        Assert.Equal(action.Id, stopOperation.ProviderParkingActionId);
        Assert.Equal(1, stopOperation.AttemptCount);
        Assert.Single(await verifyContext.NotificationEvents
            .Where(x => x.Type == NotificationEventType.VisitStopped && x.AggregateId == visit.Id)
            .ToListAsync(cancellationToken));
    }



    [Fact]
    public async Task Unknown_provider_stop_keeps_visit_stopping_and_capacity_occupied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "unknown-stop", "UNKNOWN-STOP", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "US-55-OP", "US55OP", null);
        var startOperationId = Guid.NewGuid();
        var stopOperationId = Guid.NewGuid();
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var endAt = startAt.AddHours(2);
        var visit = new Visit(Guid.NewGuid(), startOperationId, user.Id, vehicle.Id, user.Id, startAt, endAt, snapshot);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using (var startScope = provider.CreateAsyncScope())
        {
            var startStore = startScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var startResultStore = startScope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            var preparation = await startStore.PrepareAttemptAsync(
                new StartVisitClaimResult(visit, false, true), endAt, cancellationToken);
            await startResultStore.RecordConfirmedAsync(
                preparation,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    "provider-unknown-stop-1", "US55OP", startAt, endAt, "Oss", "active"),
                cancellationToken);
        }

        StopVisitClaim stopClaim;
        await using (var claimScope = provider.CreateAsyncScope())
        {
            stopClaim = await claimScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>().ClaimAsync(
                new StopVisitCommand(stopOperationId, visit.Id, user.Id),
                cancellationToken);
        }

        ProviderStopPreparation stopPreparation;
        await using (var prepareScope = provider.CreateAsyncScope())
        {
            stopPreparation = await prepareScope.ServiceProvider.GetRequiredService<IProviderStopStore>()
                .PrepareAttemptAsync(stopClaim, cancellationToken);
        }

        await using (var resultScope = provider.CreateAsyncScope())
        {
            await resultScope.ServiceProvider.GetRequiredService<IProviderStopResultStore>()
                .RecordUnknownAsync(stopPreparation, "network", cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var action = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        var stopOperation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == stopOperationId, cancellationToken);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Equal(VisitHealth.Reconciling, persistedVisit.Health);
        Assert.True(persistedVisit.OccupiesCapacity);
        Assert.Null(persistedVisit.ActualEndAt);
        Assert.Equal(ProviderActionState.Stopping, action.State);
        Assert.Equal(ProviderActionHealth.Unknown, action.Health);
        Assert.Equal(ProviderOperationStatus.Unknown, stopOperation.Status);
        Assert.Equal("network", stopOperation.LastErrorCode);
        Assert.False(await verifyContext.NotificationEvents.AnyAsync(
            x => x.Type == NotificationEventType.VisitStopped && x.AggregateId == visit.Id,
            cancellationToken));
    }



    [Fact]
    public async Task Unknown_provider_stop_reconciles_after_restart_without_second_stop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"stop-restart-{suffix}", $"STOP-RESTART-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RS-{suffix[..2]}-{suffix[2..4]}", $"RS{suffix[..4]}", null);
        var startOperationId = Guid.NewGuid();
        var stopOperationId = Guid.NewGuid();
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var endAt = startAt.AddHours(2);
        var visit = new Visit(
            Guid.NewGuid(), startOperationId, user.Id, vehicle.Id, user.Id,
            startAt, endAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();

        var remoteAction = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.NormalizedLicensePlate, startAt, endAt, "Oss"),
            cancellationToken);

        await using (var startScope = provider.CreateAsyncScope())
        {
            var startStore = startScope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var startResultStore = startScope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            var preparation = await startStore.PrepareAttemptAsync(
                new StartVisitClaimResult(visit, false, true), endAt, cancellationToken);
            await startResultStore.RecordConfirmedAsync(preparation, remoteAction, cancellationToken);
        }

        await using (var stopScope = provider.CreateAsyncScope())
        {
            var claimer = stopScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            var store = stopScope.ServiceProvider.GetRequiredService<IProviderStopStore>();
            var resultStore = stopScope.ServiceProvider.GetRequiredService<IProviderStopResultStore>();
            var claim = await claimer.ClaimAsync(
                new StopVisitCommand(stopOperationId, visit.Id, user.Id),
                cancellationToken);
            var preparation = await store.PrepareAttemptAsync(claim, cancellationToken);
            await resultStore.RecordUnknownAsync(preparation, "network", cancellationToken);
        }

        await parkingProvider.StopActionAsync(remoteAction.ProviderActionId, cancellationToken);

        await using (var recoveryScope = provider.CreateAsyncScope())
        {
            var claimer = recoveryScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            var store = recoveryScope.ServiceProvider.GetRequiredService<IProviderStopStore>();
            var executor = recoveryScope.ServiceProvider.GetRequiredService<StopVisitProviderExecutor>();
            var replay = await claimer.ClaimAsync(
                new StopVisitCommand(stopOperationId, visit.Id, user.Id),
                cancellationToken);
            var preparation = await store.PrepareAttemptAsync(replay, cancellationToken);
            var execution = await executor.ExecuteAsync(preparation, cancellationToken);

            Assert.False(execution.RequiresReconciliation);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var persistedAction = await verifyContext.ProviderParkingActions.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        var persistedStopOperation = await verifyContext.ProviderOperations.SingleAsync(
            x => x.OperationId == stopOperationId, cancellationToken);

        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.False(persistedVisit.OccupiesCapacity);
        Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        Assert.Equal(ProviderOperationStatus.Succeeded, persistedStopOperation.Status);
        Assert.Equal(1, persistedStopOperation.AttemptCount);
    }

    [Fact]
    public async Task Concurrent_stop_claims_with_different_operation_ids_coalesce_to_one_stop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "stop-coalesce", "STOP-COALESCE", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "SC-66-OP", "SC66OP", null);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, startAt.AddHours(1), snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        async Task<StopVisitClaim> ClaimAsync(Guid operationId)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>().ClaimAsync(
                new StopVisitCommand(operationId, visit.Id, user.Id),
                cancellationToken);
        }

        var claims = await Task.WhenAll(ClaimAsync(Guid.NewGuid()), ClaimAsync(Guid.NewGuid()));

        Assert.Single(claims, claim => !claim.IsReplay);
        Assert.Single(claims, claim => claim.IsReplay);
        Assert.Equal(claims[0].Operation!.Id, claims[1].Operation!.Id);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var stopOperations = await verifyContext.ProviderOperations
            .Where(x => x.VisitId == visit.Id && x.Type == ProviderOperationType.Stop)
            .ToListAsync(cancellationToken);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Single(stopOperations);
        Assert.Equal(ProviderOperationStatus.Pending, stopOperations[0].Status);
    }



    [Fact]
    public async Task End_time_change_retry_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-{suffix}", $"END-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ET-{suffix[..6]}", $"ET{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(1);
        var requestedEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                startAt.AddDays(-1),
                requestedEndAt.AddDays(1),
                TimeSpan.FromHours(4),
                Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var operationId = Guid.NewGuid();
        var command = new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, requestedEndAt);

        async Task<ChangeVisitEndTimeResult> ApplyAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
            return await changer.ApplyAsync(command, cancellationToken);
        }

        var first = await ApplyAsync();
        var replay = await ApplyAsync();

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Change.Id, replay.Change.Id);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var changes = await verifyContext.VisitEndTimeChanges
            .Where(x => x.OperationId == operationId)
            .ToListAsync(cancellationToken);

        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.Single(changes);
        Assert.NotNull(changes[0].PreviousDesiredEndAt);
        Assert.True((changes[0].PreviousDesiredEndAt.GetValueOrDefault() - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.NotNull(changes[0].RequestedDesiredEndAt);
        Assert.True((changes[0].RequestedDesiredEndAt.GetValueOrDefault() - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitEndTimeChangeResult.Applied, changes[0].Result);
    }

    [Fact]
    public async Task Concurrent_end_time_changes_are_serialized_per_visit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-race-{suffix}", $"END-RACE-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ER-{suffix[..6]}", $"ER{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(1);
        var firstEndAt = startAt.AddHours(2);
        var secondEndAt = startAt.AddHours(3);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), startAt.AddDays(-1), secondEndAt.AddDays(1),
                TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        async Task<ChangeVisitEndTimeResult> ChangeAsync(DateTimeOffset desiredEndAt)
        {
            await using var scope = provider.CreateAsyncScope();
            var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
            return await changer.ApplyAsync(
                new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, desiredEndAt),
                cancellationToken);
        }

        var results = await Task.WhenAll(ChangeAsync(firstEndAt), ChangeAsync(secondEndAt));

        Assert.All(results, result => Assert.False(result.IsReplay));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var changes = await verifyContext.VisitEndTimeChanges
            .Where(x => x.VisitId == visit.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        Assert.Equal(2, changes.Count);
        Assert.All(changes, change => Assert.Equal(VisitEndTimeChangeResult.Applied, change.Result));
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True(
            (persistedVisit.DesiredEndAt.Value - firstEndAt).Duration() <= TimeSpan.FromMilliseconds(1) ||
            (persistedVisit.DesiredEndAt.Value - secondEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.NotNull(changes[^1].RequestedDesiredEndAt);
        Assert.True(
            (changes[^1].RequestedDesiredEndAt.GetValueOrDefault() - persistedVisit.DesiredEndAt.Value).Duration()
            <= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Shortening_future_end_without_provider_action_is_local()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-short-{suffix}", $"END-SHORT-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EH-{suffix[..6]}", $"EH{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(3);
        var requestedEndAt = startAt.AddHours(1);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                startAt.AddDays(-1),
                originalEndAt.AddDays(1),
                TimeSpan.FromHours(4),
                Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();

        var result = await changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, requestedEndAt),
            cancellationToken);

        Assert.False(result.IsReplay);
        Assert.Equal(VisitEndTimeChangeResult.Applied, result.Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var providerActionCount = await verifyContext.ProviderParkingActions
            .CountAsync(x => x.VisitId == visit.Id, cancellationToken);

        Assert.Equal(VisitStatus.Active, persistedVisit.Status);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, providerActionCount);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public async Task Shortening_respects_active_provider_action_boundary(int minutesFromActionEnd, bool expectRejected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-boundary-{suffix}", $"END-BOUNDARY-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EB-{suffix[..6]}", $"EB{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var actionEndAt = startAt.AddHours(1);
        var originalEndAt = startAt.AddHours(2);
        var requestedEndAt = actionEndAt.AddMinutes(minutesFromActionEnd);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, actionEndAt);
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", startAt, "active");

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                startAt.AddDays(-1),
                originalEndAt.AddDays(1),
                TimeSpan.FromHours(4),
                Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
        var command = new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, requestedEndAt);

        if (expectRejected)
            await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ApplyAsync(command, cancellationToken));
        else
            Assert.Equal(VisitEndTimeChangeResult.Applied, (await changer.ApplyAsync(command, cancellationToken)).Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var change = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.OperationId == command.OperationId, cancellationToken);

        Assert.Equal(expectRejected ? VisitEndTimeChangeResult.Rejected : VisitEndTimeChangeResult.Applied, change.Result);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        var expectedEndAt = expectRejected ? originalEndAt : requestedEndAt;
        Assert.True((persistedVisit.DesiredEndAt.Value - expectedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task End_time_change_rejects_paid_duration_over_snapshot_limit_atomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-paid-{suffix}", $"END-PAID-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EP-{suffix[..6]}", $"EP{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var originalEndAt = startAt.AddHours(1);
        var requestedEndAt = startAt.AddHours(3);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(2), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                startAt.AddDays(-1),
                requestedEndAt.AddDays(1),
                TimeSpan.FromHours(4),
                new[] { new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)) }));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, requestedEndAt),
            cancellationToken));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var changes = await verifyContext.VisitEndTimeChanges
            .Where(x => x.VisitId == visit.Id)
            .ToListAsync(cancellationToken);

        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        var rejected = Assert.Single(changes);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, rejected.Result);
        Assert.Equal(user.Id, rejected.ActorUserId);
    }

    [Fact]
    public async Task Rejected_end_time_change_retry_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-reject-{suffix}", $"END-REJECT-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EJ-{suffix[..6]}", $"EJ{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(1);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var operationId = Guid.NewGuid();

        async Task ApplyAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
            await changer.ApplyAsync(
                new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, null),
                cancellationToken);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(ApplyAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(ApplyAsync);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var changes = await verifyContext.VisitEndTimeChanges
            .Where(x => x.OperationId == operationId)
            .ToListAsync(cancellationToken);

        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        var rejected = Assert.Single(changes);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, rejected.Result);
    }

    [Fact]
    public async Task Stop_claim_prevents_later_end_time_change()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"end-stop-{suffix}", $"END-STOP-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ES-{suffix[..6]}", $"ES{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(1);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        await using (var stopScope = provider.CreateAsyncScope())
        {
            var claimer = stopScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            await claimer.ClaimAsync(
                new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id),
                cancellationToken);
        }

        await using (var changeScope = provider.CreateAsyncScope())
        {
            var changer = changeScope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ApplyAsync(
                new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, startAt.AddHours(2)),
                cancellationToken));
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        var rejected = await verifyContext.VisitEndTimeChanges
            .SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, rejected.Result);
        Assert.Equal(user.Id, rejected.ActorUserId);
    }

    private async Task ClearVisitsAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderOperations.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderParkingActions.ExecuteDeleteAsync(cancellationToken);
        await context.Visits.ExecuteDeleteAsync(cancellationToken);
        await context.PaidWindows.ExecuteDeleteAsync(cancellationToken);
        await context.ParkingCalendarExceptions.ExecuteDeleteAsync(cancellationToken);
        await context.ParkingRuleSets.ExecuteDeleteAsync(cancellationToken);
    }

}
