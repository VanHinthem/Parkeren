using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
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
    public async Task Database_can_be_created_from_current_model()
    {
        await using var context = fixture.CreateDbContext();

        Assert.True(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
        Assert.True(await context.Users.AnyAsync(TestContext.Current.CancellationToken));
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
            return await claimer.TryClaimAsync(visit, 1, 1, cancellationToken);
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
    public async Task Visit_capacity_claim_allows_only_one_start_for_same_user_limit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        var user = new User(Guid.NewGuid(), "visitor-user-limit", "VISITOR-USER-LIMIT", "hash", UserRole.Visitor);
        var vehicle1 = new Vehicle(Guid.NewGuid(), "UL-11-AA", "UL11AA", null);
        var vehicle2 = new Vehicle(Guid.NewGuid(), "UL-22-BB", "UL22BB", null);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.AddRange(vehicle1, vehicle2);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var startAt = DateTimeOffset.UtcNow;
        var visit1 = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle1.Id, user.Id, startAt, startAt.AddHours(1), snapshot);
        var visit2 = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle2.Id, user.Id, startAt, startAt.AddHours(1), snapshot);

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
            return await claimer.TryClaimAsync(visit, 5, 1, cancellationToken);
        }

        var claims = await Task.WhenAll(ClaimAsync(visit1), ClaimAsync(visit2));

        Assert.Single(claims, claim => claim.Claimed);
        Assert.Single(claims, claim => !claim.Claimed);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(1, await verifyContext.Visits.CountAsync(
            visit => visit.UserId == user.Id && visit.Status != VisitStatus.Completed && visit.Status != VisitStatus.Cancelled,
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
            return await claimer.TryClaimAsync(visit, 5, 5, cancellationToken);
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
    public async Task Eight_hour_visit_starts_with_four_hour_provider_action_and_scheduled_followup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"eight-hour-{suffix}", $"EIGHT-HOUR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EH-{suffix[..2]}-{suffix[2..4]}", $"EH{suffix[..4]}", null);
        var businessZone = TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId);
        var localStart = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Unspecified);
        var startAt = new DateTimeOffset(localStart, businessZone.GetUtcOffset(localStart)).ToUniversalTime();
        var desiredEndAt = startAt.AddHours(8);
        var rules = new ParkingRuleSet(Guid.NewGuid(), startAt.AddDays(-1), null,
            TimeSpan.FromHours(4), Enumerable.Range(0, 7)
                .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
                .ToArray());

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.ParkingRuleSets.Add(rules);
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
        StartVisitFlowResult? result;
        await using (var scope = provider.CreateAsyncScope())
        {
            var command = new StartVisitCommand(Guid.NewGuid(), user.Id, user.Id,
                vehicle.Id, startAt, desiredEndAt);
            var context = new StartVisitContext(new(user.Id, UserRole.Visitor, true),
                new(user.Id, true), new(vehicle.Id, true, true));
            result = await scope.ServiceProvider.GetRequiredService<StartVisitFlow>().StartAsync(
                command, context,
                new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true),
                [rules], desiredEndAt, 5,
                new StartVisitProviderContext(vehicle.NormalizedLicensePlate, "Oss"),
                cancellationToken);
        }

        Assert.NotNull(result);
        Assert.Equal(StartVisitFlowOutcome.Active, result.Outcome);
        await using var verifyContext = fixture.CreateDbContext();
        var action = await verifyContext.ProviderParkingActions.SingleAsync(
            x => x.VisitId == result.Visit.Id, cancellationToken);
        var work = await verifyContext.VisitSchedulerWork.SingleAsync(
            x => x.VisitId == result.Visit.Id, cancellationToken);
        Assert.InRange((action.PlannedEndAt - startAt.AddHours(4)).Duration(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(result.Visit.DesiredEndAt);
        Assert.InRange((result.Visit.DesiredEndAt.Value - desiredEndAt).Duration(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.InRange((work.DueAt - startAt.AddHours(4).AddMinutes(-5)).Duration(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Confirmed_provider_start_schedules_coverage_at_action_boundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"scheduled-{suffix}", $"SCHEDULED-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SC-{suffix[..2]}-{suffix[2..4]}", $"SC{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var actionEndAt = now.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now, now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), now.AddDays(-1), null, TimeSpan.FromHours(4),
                Enumerable.Range(0, 7)
                    .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
                    .ToArray()));
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
                new StartVisitClaimResult(visit, false, true), actionEndAt, cancellationToken);
            await resultStore.RecordConfirmedAsync(preparation,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    $"provider-{suffix}", vehicle.NormalizedLicensePlate, now, actionEndAt, "Oss", "active"),
                cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var work = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        Assert.Equal(VisitStatus.Active, persistedVisit.Status);
        Assert.Equal(VisitSchedulerWorkType.ContinueProviderCoverage, work.Type);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, work.Status);
        Assert.InRange((work.DueAt - actionEndAt.AddMinutes(-5)).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Confirmed_evening_start_schedules_followup_at_next_paid_window()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"overnight-{suffix}", $"OVERNIGHT-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"OV-{suffix}", $"OV{suffix}".ToUpperInvariant(), null);
        var start = new DateTimeOffset(2026, 9, 28, 17, 0, 0, TimeSpan.Zero);
        var paidEnd = start.AddHours(1);
        var nextPaidStart = new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero);
        var desiredEnd = nextPaidStart.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, desiredEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(16), true));
        var rules = new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1), null, TimeSpan.FromHours(4),
            [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
             new PaidWindow(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(20, 0))]);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(rules);
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
            var store = scope.ServiceProvider.GetRequiredService<IProviderStartStore>();
            var results = scope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            var preparation = await store.PrepareAttemptAsync(
                new StartVisitClaimResult(visit, false, true), paidEnd, cancellationToken);
            await results.RecordConfirmedAsync(preparation,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    $"provider-{suffix}", vehicle.LicensePlate, start, paidEnd, "Oss", "active"),
                cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var work = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.VisitId == visit.Id, cancellationToken);
        Assert.InRange((work.DueAt - nextPaidStart).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
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
        var notificationEvent = await verifyContext.NotificationEvents.SingleAsync(
            x => x.Type == NotificationEventType.VisitStarted && x.AggregateId == visit.Id,
            cancellationToken);
        var activeAdminIds = await verifyContext.Users
            .Where(x => x.IsActive && x.Role == UserRole.Admin)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var inboxNotifications = await verifyContext.Notifications
            .Where(x => x.SourceEventId == notificationEvent.Id)
            .ToListAsync(cancellationToken);

        Assert.Single(inboxNotifications, x => x.RecipientUserId == user.Id);
        Assert.All(activeAdminIds, adminId =>
            Assert.Single(inboxNotifications, x => x.RecipientUserId == adminId));
        Assert.Equal(activeAdminIds.Append(user.Id).Distinct().Count(), inboxNotifications.Count);
        Assert.All(inboxNotifications, notification =>
        {
            Assert.Equal(NotificationType.VisitStarted, notification.Type);
            Assert.Equal(visit.Id, notification.VisitId);
            Assert.False(notification.IsRead);
        });
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
            var claim = await capacity.TryClaimAsync(proposedVisit, 5, 5, cancellationToken);
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
            var replay = await capacity.TryClaimAsync(duplicateProposal, 5, 5, cancellationToken);

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

            await Assert.ThrowsAnyAsync<Exception>(() => capacity.TryClaimAsync(visit, 1, 1, cancellationToken));
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
            var retry = await capacity.TryClaimAsync(retryVisit, 1, 1, cancellationToken);

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

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Equal(VisitHealth.Healthy, persistedVisit.Health);
        Assert.True(persistedVisit.OccupiesCapacity);
        Assert.Equal(ProviderActionState.Stopped, action.State);
        Assert.Equal(ProviderActionHealth.Healthy, action.Health);
        Assert.Equal(ProviderOperationStatus.Succeeded, stopOperation.Status);
        Assert.Equal(action.Id, stopOperation.ProviderParkingActionId);
        Assert.Equal(1, stopOperation.AttemptCount);
        Assert.Empty(await verifyContext.NotificationEvents
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

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.True(persistedVisit.OccupiesCapacity);
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
    public async Task Parking_rule_persists_action_duration_and_continuation_strategy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var ossRules = new ParkingRuleSet(Guid.NewGuid(), now.AddDays(-1), null,
            TimeSpan.FromHours(4), Array.Empty<PaidWindow>());
        var extendingRules = new ParkingRuleSet(Guid.NewGuid(), now.AddDays(-1), null,
            TimeSpan.FromHours(6), Array.Empty<PaidWindow>(),
            continuation: ProviderCoverageContinuation.ExtendAction);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.ParkingRuleSets.AddRange(ossRules, extendingRules);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.ParkingRuleSets
            .Where(x => x.Id == ossRules.Id || x.Id == extendingRules.Id)
            .ToListAsync(cancellationToken);
        Assert.Equal(TimeSpan.FromHours(4), persisted.Single(x => x.Id == ossRules.Id).MaxProviderActionDuration);
        Assert.Equal(ProviderCoverageContinuation.StartNewAction,
            persisted.Single(x => x.Id == ossRules.Id).Continuation);
        Assert.Equal(ProviderCoverageContinuation.ExtendAction,
            persisted.Single(x => x.Id == extendingRules.Id).Continuation);
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
        Assert.NotNull(changes[^1].PreviousDesiredEndAt);
        Assert.NotNull(changes[0].RequestedDesiredEndAt);
        Assert.True(
            (changes[^1].PreviousDesiredEndAt.GetValueOrDefault() - changes[0].RequestedDesiredEndAt.GetValueOrDefault()).Duration()
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

    [Fact]
    public async Task Extending_beyond_active_provider_action_does_not_precreate_future_action()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-beyond-{suffix}", $"EXTEND-BEYOND-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EX-{suffix[..6]}", $"EX{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddMinutes(30);
        var actionEndAt = startAt.AddHours(1);
        var requestedEndAt = actionEndAt.AddHours(1);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, actionEndAt);
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
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();

        var result = await changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, requestedEndAt),
            cancellationToken);

        Assert.Equal(VisitEndTimeChangeResult.Applied, result.Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.Equal(snapshot, persistedVisit.PolicySnapshot);

        var actions = await verifyContext.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken);
        var persistedAction = Assert.Single(actions);
        Assert.Equal(ProviderActionState.Active, persistedAction.State);
        Assert.Equal(action.ProviderActionId, persistedAction.ProviderActionId);
        Assert.True((persistedAction.PlannedEndAt - actionEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Extending_within_active_provider_action_is_local()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-local-{suffix}", $"EXTEND-LOCAL-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EL-{suffix[..6]}", $"EL{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddMinutes(30);
        var requestedEndAt = startAt.AddMinutes(45);
        var actionEndAt = startAt.AddHours(1);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, actionEndAt);
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
                actionEndAt.AddDays(1),
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

        Assert.Equal(VisitEndTimeChangeResult.Applied, result.Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));

        var actions = await verifyContext.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken);
        var persistedAction = Assert.Single(actions);
        Assert.Equal(ProviderActionState.Active, persistedAction.State);
        Assert.Equal(action.ProviderActionId, persistedAction.ProviderActionId);
        Assert.True((persistedAction.PlannedEndAt - actionEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task End_time_extension_from_free_into_paid_window_counts_paid_duration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"free-paid-{suffix}", $"FREE-PAID-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"FP-{suffix[..6]}", $"FP{suffix[..6]}".ToUpperInvariant(), null);
        var businessZone = TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId);
        var localStart = new DateTime(2026, 9, 28, 8, 30, 0, DateTimeKind.Unspecified);
        var startAt = new DateTimeOffset(localStart, businessZone.GetUtcOffset(localStart)).ToUniversalTime();
        var originalEndAt = startAt.AddMinutes(30);
        var requestedEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromMinutes(30), TimeSpan.FromHours(8), true, true);
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
        var operationId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, requestedEndAt),
            cancellationToken));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));

        var change = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, change.Result);
    }

    [Fact]
    public async Task End_time_extension_uses_rules_after_ruleset_boundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"rules-boundary-{suffix}", $"RULES-BOUNDARY-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RB-{suffix[..6]}", $"RB{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var boundary = startAt.AddMinutes(30);
        var originalEndAt = boundary;
        var requestedEndAt = boundary.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(1), TimeSpan.FromHours(8), true, true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        var localBoundary = TimeZoneInfo.ConvertTime(boundary, TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId));
        var paidWindow = new PaidWindow(localBoundary.DayOfWeek, TimeOnly.MinValue, new TimeOnly(23, 59, 59));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.AddRange(
                new ParkingRuleSet(Guid.NewGuid(), startAt.AddDays(-1), boundary, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()),
                new ParkingRuleSet(Guid.NewGuid(), boundary, requestedEndAt.AddDays(1), TimeSpan.FromHours(4), new[] { paidWindow }));
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
        var operationId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, requestedEndAt),
            cancellationToken));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));

        var change = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, change.Result);
    }

    [Fact]
    public async Task End_time_change_rejects_elapsed_duration_over_snapshot_limit_atomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"elapsed-limit-{suffix}", $"ELAPSED-LIMIT-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ED-{suffix[..6]}", $"ED{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddMinutes(30);
        var requestedEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(1), true, true);
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
                TimeSpan.FromHours(8),
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
        var operationId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, requestedEndAt),
            cancellationToken));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));

        var change = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, change.Result);
    }

    [Fact]
    public async Task End_time_change_by_admin_preserves_visit_owner_and_policy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = new User(Guid.NewGuid(), $"owner-{suffix}", $"OWNER-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}".ToUpperInvariant(), "hash", UserRole.Admin);
        var vehicle = new Vehicle(Guid.NewGuid(), $"AO-{suffix[..6]}", $"AO{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(1);
        var requestedEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), owner.Id, vehicle.Id, owner.Id, startAt, originalEndAt, snapshot);
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.AddRange(owner, admin);
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
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
        var operationId = Guid.NewGuid();

        await changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(operationId, visit.Id, admin.Id, requestedEndAt),
            cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var change = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.OperationId == operationId, cancellationToken);

        Assert.Equal(owner.Id, persistedVisit.UserId);
        Assert.Equal(snapshot, persistedVisit.PolicySnapshot);
        Assert.Equal(admin.Id, change.ActorUserId);
        Assert.Equal(VisitEndTimeChangeResult.Applied, change.Result);
    }

    [Fact]
    public async Task End_time_at_visit_start_is_rejected_and_audited_atomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"invalid-end-{suffix}", $"INVALID-END-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"IE-{suffix[..6]}", $"IE{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true);
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
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();
        var operationId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, startAt),
            cancellationToken));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - originalEndAt).Duration() <= TimeSpan.FromMilliseconds(1));

        var changes = await verifyContext.VisitEndTimeChanges
            .Where(x => x.OperationId == operationId)
            .ToListAsync(cancellationToken);
        var change = Assert.Single(changes);
        Assert.Equal(VisitEndTimeChangeResult.Rejected, change.Result);
        Assert.Equal(user.Id, change.ActorUserId);
    }

    [Fact]
    public async Task Concrete_visit_can_change_to_manual_end_when_snapshot_allows_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"concrete-manual-{suffix}", $"CONCRETE-MANUAL-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"CM-{suffix[..6]}", $"CM{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var originalEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true);
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
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();

        var result = await changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, null),
            cancellationToken);

        Assert.Equal(VisitEndTimeChangeResult.Applied, result.Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.Null(persistedVisit.DesiredEndAt);
        Assert.Equal(snapshot, persistedVisit.PolicySnapshot);
        Assert.Empty(await verifyContext.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Manual_visit_can_change_to_concrete_end_without_precreating_provider_actions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"manual-concrete-{suffix}", $"MANUAL-CONCRETE-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"MC-{suffix[..6]}", $"MC{suffix[..6]}".ToUpperInvariant(), null);
        var startAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var requestedEndAt = startAt.AddHours(2);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, startAt, null, snapshot);
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
        await using var scope = provider.CreateAsyncScope();
        var changer = scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>();

        var result = await changer.ApplyAsync(
            new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, user.Id, requestedEndAt),
            cancellationToken);

        Assert.Equal(VisitEndTimeChangeResult.Applied, result.Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        Assert.Equal(snapshot, persistedVisit.PolicySnapshot);
        Assert.Empty(await verifyContext.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public async Task Shortening_respects_active_provider_action_boundary(int minutesFromActionEnd, bool expectScheduledStop)
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

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, actionEndAt);
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

        Assert.Equal(
            VisitEndTimeChangeResult.Applied,
            (await changer.ApplyAsync(command, cancellationToken)).Change.Result);

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var change = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.OperationId == command.OperationId, cancellationToken);

        Assert.Equal(VisitEndTimeChangeResult.Applied, change.Result);
        Assert.NotNull(persistedVisit.DesiredEndAt);
        Assert.True((persistedVisit.DesiredEndAt.Value - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));

        var scheduledStops = await verifyContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id &&
                        x.Type == VisitSchedulerWorkType.StopVisit &&
                        x.Status == VisitSchedulerWorkStatus.Pending)
            .ToListAsync(cancellationToken);

        if (expectScheduledStop)
        {
            var stopWork = Assert.Single(scheduledStops);
            Assert.True((stopWork.DueAt - requestedEndAt).Duration() <= TimeSpan.FromMilliseconds(1));
        }
        else
        {
            Assert.Empty(scheduledStops);
        }
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

    [Fact]
    public async Task Stop_claim_cancels_pending_scheduler_work()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"stop-scheduler-{suffix}", $"STOP-SCHEDULER-{suffix}".ToUpperInvariant(), "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SS-{suffix[..6]}", $"SS{suffix[..6]}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, now.AddHours(-1), now.AddHours(2), snapshot);
        visit.Activate();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(5));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
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
            var claimer = scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>();
            await claimer.ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var persistedWork = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled, persistedWork.Status);
        Assert.Null(persistedWork.ClaimedAt);
        Assert.Null(persistedWork.ClaimedBy);
    }

    [Fact]
    public async Task Scheduler_work_claim_allows_only_one_worker_to_claim_due_item()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "scheduler-visitor", "SCHEDULER-VISITOR", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "SC-11-HD", "SC11HD", null);
        var now = DateTimeOffset.UtcNow;
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, now.AddHours(-1), now.AddHours(2), snapshot);
        visit.Activate();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(-1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
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

        async Task<VisitSchedulerWork?> ClaimAsync(string workerId)
        {
            await using var scope = provider.CreateAsyncScope();
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
            return await claimer.ClaimNextDueAsync(workerId, now, cancellationToken);
        }

        var claims = await Task.WhenAll(ClaimAsync("worker-1"), ClaimAsync("worker-2"));

        Assert.Single(claims, claim => claim is not null);

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
        Assert.Equal(VisitSchedulerWorkStatus.Claimed, persisted.Status);
        Assert.NotNull(persisted.ClaimedAt);
        Assert.Contains(persisted.ClaimedBy, new[] { "worker-1", "worker-2" });
    }

    [Fact]
    public async Task Failed_scheduler_work_is_released_only_by_its_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"failed-work-{suffix}", $"FAILED-WORK-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"FW-{suffix}", $"FW{suffix}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(-1));
        work.Claim("worker-owner", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
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
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
            await claimer.ReleaseFailedAsync(work.Id, "other-worker", now.AddMinutes(1), cancellationToken);
        }

        await using (var intermediate = fixture.CreateDbContext())
        {
            var stillClaimed = await intermediate.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            Assert.Equal(VisitSchedulerWorkStatus.Claimed, stillClaimed.Status);
            Assert.Equal("worker-owner", stillClaimed.ClaimedBy);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
            await claimer.ReleaseFailedAsync(work.Id, "worker-owner", now.AddMinutes(1), cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, persisted.Status);
        Assert.Null(persisted.ClaimedBy);
        Assert.InRange((persisted.DueAt - now.AddMinutes(1)).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduler_checks_previous_provider_action_before_starting_a_new_one(bool externallyStopped)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"oss-next-{suffix}", $"OSS-NEXT-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ON-{suffix[..2]}-{suffix[2..4]}", $"ON{suffix[..4]}", null);
        var boundary = DateTimeOffset.UtcNow.AddMinutes(-1);
        var startAt = boundary.AddHours(-4);
        var providerPrevious = await parkingProvider.StartActionAsync(
            new Parkeren.Application.ParkingProvider.ProviderParkingActionRequest(vehicle.LicensePlate, startAt, boundary, "Oss"),
            cancellationToken);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, boundary.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var previous = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, boundary);
        previous.MarkStarting();
        previous.MarkActive(providerPrevious.ProviderActionId, startAt);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, boundary.AddMinutes(-5));
        work.Claim("worker-oss-next", DateTimeOffset.UtcNow);
        var windows = Enumerable.Range(0, 7)
            .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
            .ToArray();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(previous);
            seedContext.VisitSchedulerWork.Add(work);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), startAt.AddDays(-1), null, TimeSpan.FromHours(4), windows));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        if (externallyStopped)
            (await http.PostAsync($"api/test/actions/{providerPrevious.ProviderActionId}/stop", null, cancellationToken))
                .EnsureSuccessStatusCode();

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
            ["ParkingProvider:Location"] = "Oss"
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(externallyStopped ? VisitSchedulerWorkStatus.Cancelled : VisitSchedulerWorkStatus.Completed,
            (await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken)).Status);
        Assert.Equal(externallyStopped ? 1 : 2,
            await verifyContext.ProviderParkingActions.CountAsync(x => x.VisitId == visit.Id, cancellationToken));
        var operations = await verifyContext.ProviderOperations.Where(x => x.VisitId == visit.Id)
            .ToListAsync(cancellationToken);
        if (externallyStopped)
        {
            Assert.Empty(operations);
            Assert.Equal(VisitHealth.AttentionRequired,
                (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
            Assert.Equal(ProviderActionState.Stopped,
                (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == previous.Id, cancellationToken)).State);
        }
        else
        {
            var operation = Assert.Single(operations);
            Assert.Equal(ProviderOperationType.ContinueStart, operation.Type);
            Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        }
        Assert.Equal(externallyStopped ? 1 : 2, (await parkingProvider.GetActionsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Scheduled_stop_stops_active_provider_action_and_completes_visit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"scheduled-stop-{suffix}", $"SCHEDULED-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SS-{suffix[..4]}", $"SS{suffix[..4]}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddMinutes(-30);
        var desiredEndAt = now.AddMinutes(-1);
        var providerEndAt = now.AddHours(1);
        var remote = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.NormalizedLicensePlate, startAt, providerEndAt, "Oss"),
            cancellationToken);

        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, desiredEndAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, providerEndAt);
        action.MarkStarting();
        action.MarkActive(remote.ProviderActionId, startAt);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.StopVisit, desiredEndAt);
        work.Claim("worker-scheduled-stop", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.VisitSchedulerWork.Add(work);
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

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persistedVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var persistedWork = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
        var persistedAction = await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
        var stopOperation = await verifyContext.ProviderOperations.SingleAsync(
            x => x.OperationId == work.Id && x.Type == ProviderOperationType.Stop,
            cancellationToken);

        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.False(persistedVisit.OccupiesCapacity);
        Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedWork.Status);
        Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        Assert.Equal(ProviderOperationStatus.Succeeded, stopOperation.Status);

        var remoteActions = await parkingProvider.GetActionsAsync(cancellationToken);
        var stoppedRemote = Assert.Single(remoteActions, x => x.ProviderActionId == remote.ProviderActionId);
        Assert.Equal("stopped", stoppedRemote.Status, ignoreCase: true);
    }

    [Fact]
    public async Task Periodic_provider_check_blocks_continuation_after_external_stop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"monitor-{suffix}", $"MONITOR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"MO-{suffix}", $"MO{suffix}".ToUpperInvariant(), null);
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        var end = start.AddHours(2);
        var remote = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.NormalizedLicensePlate, start, end, "Oss"), cancellationToken);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, end.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.MarkActive(remote.ProviderActionId, start);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, end.AddMinutes(-5));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.VisitSchedulerWork.Add(work);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        (await http.PostAsync($"api/test/actions/{remote.ProviderActionId}/stop", null, cancellationToken))
            .EnsureSuccessStatusCode();
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileActiveProviderActionsAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(ProviderActionState.Stopped,
            (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken)).State);
        Assert.Equal(VisitHealth.AttentionRequired,
            (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled,
            (await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Restart_rebuilds_missing_work_at_next_paid_window()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"recover-gap-{suffix}", $"RECOVER-GAP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RG-{suffix}", $"RG{suffix}".ToUpperInvariant(), null);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var actionEnd = start.AddHours(2);
        var nextPaidStart = actionEnd.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, nextPaidStart.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var remote = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.NormalizedLicensePlate, start, actionEnd, "Oss"), cancellationToken);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, actionEnd);
        action.MarkStarting();
        action.MarkActive(remote.ProviderActionId, start);
        var paidWindows = Enumerable.Range(0, 7)
            .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
            .ToArray();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), start.AddDays(-1), actionEnd, TimeSpan.FromHours(4), paidWindows));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), actionEnd, nextPaidStart, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), nextPaidStart, null, TimeSpan.FromHours(4), paidWindows));
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
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>().RecoverAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        var work = Assert.Single(await verifyContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken));
        Assert.Equal(VisitSchedulerWorkStatus.Pending, work.Status);
        Assert.InRange((work.DueAt - nextPaidStart).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Restart_blocks_continuation_after_provider_action_was_stopped_externally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"external-stop-{suffix}", $"EXTERNAL-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"XS-{suffix}", $"XS{suffix}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-1);
        var end = now.AddHours(1);
        var remote = await parkingProvider.StartActionAsync(
            new Parkeren.Application.ParkingProvider.ProviderParkingActionRequest(vehicle.LicensePlate, start, end, "Oss"),
            cancellationToken);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, end.AddHours(1), new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.MarkActive(remote.ProviderActionId, start);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, end.AddMinutes(-5));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.VisitSchedulerWork.Add(work);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        (await http.PostAsync($"api/test/actions/{remote.ProviderActionId}/stop", null, cancellationToken))
            .EnsureSuccessStatusCode();
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>().RecoverAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(ProviderActionState.Stopped,
            (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken)).State);
        Assert.Equal(VisitHealth.AttentionRequired,
            (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled,
            (await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken)).Status);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Restart_keeps_entirely_free_visit_healthy_without_provider_action()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"free-restart-{suffix}", $"FREE-RESTART-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RF-{suffix}", $"RF{suffix}".ToUpperInvariant(), null);
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, start.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), start.AddDays(-1), null, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
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
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>().RecoverAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(VisitHealth.Healthy,
            (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
        Assert.Empty(await verifyContext.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Free_visit_starts_first_provider_action_at_paid_boundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"free-paid-{suffix}", $"FREE-PAID-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"FP-{suffix}", $"FP{suffix}".ToUpperInvariant(), null);
        var boundary = DateTimeOffset.UtcNow.AddMinutes(-1);
        var start = boundary.AddHours(-1);
        var desiredEnd = boundary.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, desiredEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var paidWindows = Enumerable.Range(0, 7)
            .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
            .ToArray();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), start.AddDays(-1), boundary, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), boundary, null, TimeSpan.FromHours(4), paidWindows));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
            ["ParkingProvider:Location"] = "Oss"
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var persistedVisit = await context.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitStartStore>().SaveAsync(persistedVisit, cancellationToken);
        }

        await using (var recoveryScope = provider.CreateAsyncScope())
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>().RecoverAsync(cancellationToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
            var work = await claimer.ClaimNextDueAsync("free-paid-worker", DateTimeOffset.UtcNow, cancellationToken);
            Assert.NotNull(work);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(work, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var action = Assert.Single(await verifyContext.ProviderParkingActions.Where(x => x.VisitId == visit.Id)
            .ToListAsync(cancellationToken));
        Assert.Equal(ProviderActionState.Active, action.State);
        Assert.InRange((action.PlannedStartAt - boundary).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitHealth.Healthy,
            (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Scheduler_skips_free_gap_and_caps_next_action_at_paid_boundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"paid-gap-{suffix}", $"PAID-GAP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"PG-{suffix}", $"PG{suffix}".ToUpperInvariant(), null);
        var nextPaidStart = DateTimeOffset.UtcNow.AddMinutes(-1);
        var firstPaidEnd = nextPaidStart.AddHours(-1);
        var nextPaidEnd = nextPaidStart.AddHours(1);
        var start = firstPaidEnd.AddHours(-1);
        var desiredEnd = nextPaidEnd.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, desiredEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var previousRemote = await parkingProvider.StartActionAsync(
            new Parkeren.Application.ParkingProvider.ProviderParkingActionRequest(
                vehicle.LicensePlate, start, firstPaidEnd, "Oss"), cancellationToken);
        var previous = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, firstPaidEnd);
        previous.MarkStarting();
        previous.MarkActive(previousRemote.ProviderActionId, start);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, nextPaidStart);
        work.Claim("gap-worker", DateTimeOffset.UtcNow);
        var paidWindows = Enumerable.Range(0, 7)
            .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
            .ToArray();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(previous);
            seedContext.VisitSchedulerWork.Add(work);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), start.AddDays(-1), firstPaidEnd, TimeSpan.FromHours(4), paidWindows));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), firstPaidEnd, nextPaidStart, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), nextPaidStart, nextPaidEnd, TimeSpan.FromHours(4), paidWindows));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), nextPaidEnd, null, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
            ["ParkingProvider:Location"] = "Oss"
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(ProviderActionState.Completed,
            (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == previous.Id, cancellationToken)).State);
        var next = Assert.Single(await verifyContext.ProviderParkingActions
            .Where(x => x.VisitId == visit.Id && x.Id != previous.Id).ToListAsync(cancellationToken));
        Assert.Equal(ProviderActionState.Active, next.State);
        Assert.InRange((next.PlannedStartAt - nextPaidStart).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.InRange((next.PlannedEndAt - nextPaidEnd).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, (await parkingProvider.GetActionsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Restart_blocks_continuation_after_provider_action_end_changed_externally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"external-end-{suffix}", $"EXTERNAL-END-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"XE-{suffix}", $"XE{suffix}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-1);
        var end = now.AddHours(1);
        var remote = await parkingProvider.StartActionAsync(
            new Parkeren.Application.ParkingProvider.ProviderParkingActionRequest(vehicle.LicensePlate, start, end, "Oss"),
            cancellationToken);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, end.AddHours(1), new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.MarkActive(remote.ProviderActionId, start);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, end.AddMinutes(-5));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.VisitSchedulerWork.Add(work);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        (await http.PutAsJsonAsync($"api/test/actions/{remote.ProviderActionId}/end",
            new { End = end.AddMinutes(-15) }, cancellationToken)).EnsureSuccessStatusCode();
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>().RecoverAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(VisitHealth.AttentionRequired,
            (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Health);
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled,
            (await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken)).Status);
        var localEnd = (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken)).PlannedEndAt;
        Assert.InRange((localEnd - end).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Periodic_check_reconciles_unknown_continuation_start_without_second_provider_action()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"recover-next-{suffix}", $"RECOVER-NEXT-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RN-{suffix[..2]}-{suffix[2..4]}", $"RN{suffix[..4]}", null);
        var boundary = DateTimeOffset.UtcNow.AddMinutes(-1);
        var endAt = boundary.AddHours(2);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            boundary.AddHours(-4), endAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var previous = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id,
            visit.StartAt, boundary);
        previous.MarkStarting();
        previous.MarkActive($"previous-{suffix}", visit.StartAt);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, boundary.AddMinutes(-5));
        work.Claim("worker-before-restart", DateTimeOffset.UtcNow);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(previous);
            seedContext.VisitSchedulerWork.Add(work);
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
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartStore>();
            var results = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartResultStore>();
            var preparation = await store.PrepareAttemptAsync(visit, previous, work.Id, endAt, cancellationToken);
            await parkingProvider.StartActionAsync(new ProviderParkingActionRequest(
                vehicle.NormalizedLicensePlate, boundary.AddSeconds(1), endAt, "Oss"), cancellationToken);
            await results.RecordUnknownAsync(preparation, "timeout", cancellationToken);
        }

        await using (var recoveryScope = provider.CreateAsyncScope())
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileUnknownOperationsAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        var operation = await verifyContext.ProviderOperations.SingleAsync(x => x.OperationId == work.Id, cancellationToken);
        var nextAction = await verifyContext.ProviderParkingActions.SingleAsync(
            x => x.Id == operation.ProviderParkingActionId, cancellationToken);
        var recoveredVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.Equal(ProviderActionState.Active, nextAction.State);
        Assert.Equal(VisitStatus.Active, recoveredVisit.Status);
        Assert.Equal(VisitHealth.Healthy, recoveredVisit.Health);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Periodic_check_completes_pending_end_time_change_after_scheduled_replacement_reconciliation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"recover-end-{suffix}", $"RECOVER-END-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RE-{suffix[..2]}-{suffix[2..4]}", $"RE{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visitStart = now.AddHours(-1);
        var originalEnd = now.AddHours(5);
        var requestedEnd = now.AddHours(2);
        var replacementStart = now.AddMinutes(30);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            visitStart, originalEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();

        var changeOperationId = Guid.NewGuid();
        var change = new VisitEndTimeChange(
            Guid.NewGuid(), changeOperationId, visit.Id, user.Id,
            originalEnd, requestedEnd, now);

        var replacementAction = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, replacementStart, requestedEnd);
        replacementAction.MarkStarting();
        replacementAction.MarkUnknown();

        var providerOperation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, replacementAction.Id,
            ProviderOperationType.ContinueStart);
        providerOperation.SetParentOperationId(changeOperationId);
        providerOperation.SetRequestedEndAt(requestedEnd);
        providerOperation.BeginAttempt();
        providerOperation.MarkUnknown("timeout");

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitEndTimeChanges.Add(change);
            seedContext.ProviderParkingActions.Add(replacementAction);
            seedContext.ProviderOperations.Add(providerOperation);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                visitStart.AddDays(-1),
                requestedEnd.AddDays(1),
                TimeSpan.FromHours(4),
                Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var remote = await parkingProvider.StartActionAsync(new ProviderParkingActionRequest(
            vehicle.NormalizedLicensePlate, replacementStart, requestedEnd, "Oss"), cancellationToken);
        Assert.Equal("scheduled", remote.Status, ignoreCase: true);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using (var recoveryScope = provider.CreateAsyncScope())
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileUnknownOperationsAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        var recoveredOperation = await verifyContext.ProviderOperations
            .SingleAsync(x => x.Id == providerOperation.Id, cancellationToken);
        var recoveredAction = await verifyContext.ProviderParkingActions
            .SingleAsync(x => x.Id == replacementAction.Id, cancellationToken);
        var recoveredVisit = await verifyContext.Visits
            .SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var recoveredChange = await verifyContext.VisitEndTimeChanges
            .SingleAsync(x => x.Id == change.Id, cancellationToken);

        Assert.Equal(ProviderOperationStatus.Succeeded, recoveredOperation.Status);
        Assert.Equal(ProviderActionState.Scheduled, recoveredAction.State);
        Assert.Equal(remote.ProviderActionId, recoveredAction.ProviderActionId);
        Assert.InRange((recoveredVisit.DesiredEndAt!.Value - requestedEnd).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitEndTimeChangeResult.Applied, recoveredChange.Result);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Periodic_check_completes_pending_end_time_change_after_scheduled_cancel_reconciliation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"recover-cancel-{suffix}", $"RECOVER-CANCEL-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RC-{suffix[..2]}-{suffix[2..4]}", $"RC{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visitStart = now.AddHours(-1);
        var originalEnd = now.AddHours(5);
        var requestedEnd = now.AddMinutes(20);
        var scheduledStart = now.AddMinutes(30);
        var scheduledEnd = now.AddHours(4);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            visitStart, originalEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();

        var remote = await parkingProvider.StartActionAsync(new ProviderParkingActionRequest(
            vehicle.NormalizedLicensePlate, scheduledStart, scheduledEnd, "Oss"), cancellationToken);
        Assert.Equal("scheduled", remote.Status, ignoreCase: true);

        var changeOperationId = Guid.NewGuid();
        var change = new VisitEndTimeChange(
            Guid.NewGuid(), changeOperationId, visit.Id, user.Id,
            originalEnd, requestedEnd, now);

        var scheduledAction = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, scheduledStart, scheduledEnd);
        scheduledAction.MarkStarting();
        scheduledAction.MarkScheduled(remote.ProviderActionId, remote.Status);
        scheduledAction.BeginStopping();
        scheduledAction.MarkUnknown();

        var stopOperation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, scheduledAction.Id,
            ProviderOperationType.Stop);
        stopOperation.SetParentOperationId(changeOperationId);
        stopOperation.BeginAttempt();
        stopOperation.MarkUnknown("timeout");

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitEndTimeChanges.Add(change);
            seedContext.ProviderParkingActions.Add(scheduledAction);
            seedContext.ProviderOperations.Add(stopOperation);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                visitStart.AddDays(-1),
                requestedEnd.AddDays(1),
                TimeSpan.FromHours(4),
                Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        await parkingProvider.StopActionAsync(remote.ProviderActionId, cancellationToken);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using (var recoveryScope = provider.CreateAsyncScope())
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileUnknownOperationsAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        var recoveredOperation = await verifyContext.ProviderOperations
            .SingleAsync(x => x.Id == stopOperation.Id, cancellationToken);
        var recoveredAction = await verifyContext.ProviderParkingActions
            .SingleAsync(x => x.Id == scheduledAction.Id, cancellationToken);
        var recoveredVisit = await verifyContext.Visits
            .SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var recoveredChange = await verifyContext.VisitEndTimeChanges
            .SingleAsync(x => x.Id == change.Id, cancellationToken);

        Assert.Equal(ProviderOperationStatus.Succeeded, recoveredOperation.Status);
        Assert.Equal(ProviderActionState.Stopped, recoveredAction.State);
        Assert.InRange((recoveredVisit.DesiredEndAt!.Value - requestedEnd).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitEndTimeChangeResult.Applied, recoveredChange.Result);
        var remoteActions = await parkingProvider.GetActionsAsync(cancellationToken);
        var stoppedRemote = Assert.Single(remoteActions);
        Assert.Equal(remote.ProviderActionId, stoppedRemote.ProviderActionId);
        Assert.Equal("stopped", stoppedRemote.Status, ignoreCase: true);
    }

    [Fact]
    public async Task Recovery_creates_missing_replacement_once_after_successful_scheduled_cancel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"recover-gap-{suffix}", $"RECOVER-GAP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RG-{suffix[..2]}-{suffix[2..4]}", $"RG{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visitStart = now.AddHours(-1);
        var originalEnd = now.AddHours(5);
        var scheduledStart = now.AddMinutes(30);
        var requestedEnd = now.AddHours(2);
        var scheduledEnd = now.AddHours(4);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            visitStart, originalEnd,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();

        var remote = await parkingProvider.StartActionAsync(new ProviderParkingActionRequest(
            vehicle.NormalizedLicensePlate, scheduledStart, scheduledEnd, "Oss"), cancellationToken);
        await parkingProvider.StopActionAsync(remote.ProviderActionId, cancellationToken);

        var changeOperationId = Guid.NewGuid();
        var change = new VisitEndTimeChange(
            Guid.NewGuid(), changeOperationId, visit.Id, user.Id,
            originalEnd, requestedEnd, now);

        var originalAction = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, scheduledStart, scheduledEnd);
        originalAction.MarkStarting();
        originalAction.MarkScheduled(remote.ProviderActionId, remote.Status);
        originalAction.BeginStopping();
        originalAction.MarkStopped(now, "stopped");

        var cancelOperation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, originalAction.Id,
            ProviderOperationType.Stop);
        cancelOperation.SetParentOperationId(changeOperationId);
        cancelOperation.BeginAttempt();
        cancelOperation.Succeed(now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitEndTimeChanges.Add(change);
            seedContext.ProviderParkingActions.Add(originalAction);
            seedContext.ProviderOperations.Add(cancelOperation);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                visitStart.AddDays(-1),
                requestedEnd.AddDays(1),
                TimeSpan.FromHours(4),
                Array.Empty<PaidWindow>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
            ["ParkingProvider:Location"] = "Oss"
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider>(parkingProvider);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using (var recoveryScope = provider.CreateAsyncScope())
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileUnknownOperationsAsync(cancellationToken);

        await using (var replayScope = provider.CreateAsyncScope())
            await replayScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileUnknownOperationsAsync(cancellationToken);

        await using var verifyContext = fixture.CreateDbContext();
        var recoveredVisit = await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var recoveredChange = await verifyContext.VisitEndTimeChanges.SingleAsync(x => x.Id == change.Id, cancellationToken);
        var children = await verifyContext.ProviderOperations
            .Where(x => x.ParentOperationId == changeOperationId)
            .OrderBy(x => x.Type)
            .ToListAsync(cancellationToken);

        Assert.InRange((recoveredVisit.DesiredEndAt!.Value - requestedEnd).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitEndTimeChangeResult.Applied, recoveredChange.Result);
        Assert.Equal(2, children.Count);
        Assert.Single(children, x => x.Type == ProviderOperationType.Stop);
        var replacementOperation = Assert.Single(children, x => x.Type == ProviderOperationType.ContinueStart);
        Assert.Equal(ProviderOperationStatus.Succeeded, replacementOperation.Status);

        var replacementActions = await verifyContext.ProviderParkingActions
            .Where(x => x.VisitId == visit.Id && x.Id != originalAction.Id)
            .ToListAsync(cancellationToken);
        var replacementAction = Assert.Single(replacementActions);
        Assert.Equal(ProviderActionState.Scheduled, replacementAction.State);
        Assert.InRange((replacementAction.PlannedEndAt - requestedEnd).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));

        var remoteActions = await parkingProvider.GetActionsAsync(cancellationToken);
        Assert.Equal(2, remoteActions.Count);
        Assert.Single(remoteActions, x => x.ProviderActionId == remote.ProviderActionId &&
                                          string.Equals(x.Status, "stopped", StringComparison.OrdinalIgnoreCase));
        Assert.Single(remoteActions, x => string.Equals(x.Status, "scheduled", StringComparison.OrdinalIgnoreCase) &&
                                          x.ProviderActionId != remote.ProviderActionId);
    }

    [Fact]
    public async Task Scheduler_processor_reschedules_when_provider_coverage_is_still_active()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "scheduler-jit", "SCHEDULER-JIT", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "SC-33-HD", "SC33HD", null);
        var now = DateTimeOffset.UtcNow;
        var coverageEnd = now.AddMinutes(30);
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, now.AddHours(-1), now.AddHours(2), snapshot);
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, now.AddHours(-1), coverageEnd);
        action.MarkStarting();
        action.MarkActive("provider-jit", now.AddHours(-1));
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(-1));
        work.Claim("worker-jit-test", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.VisitSchedulerWork.Add(work);
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
            var processor = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>();
            var claimed = await scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>()
                .VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await processor.ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, persisted.Status);
        Assert.InRange((persisted.DueAt - coverageEnd.AddMinutes(-5)).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Null(persisted.ClaimedAt);
        Assert.Null(persisted.ClaimedBy);
    }

    [Fact]
    public async Task Open_ended_visit_without_paid_segment_rolls_scheduler_horizon_forward()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"rolling-horizon-{suffix}", $"ROLLING-HORIZON-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RH-{suffix[..2]}-{suffix[2..4]}", $"RH{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddHours(-1),
            null,
            new EffectiveParkingPolicySnapshot(null, null, true));
        visit.Activate();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now.AddMinutes(-1));
        work.Claim("rolling-horizon-test", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                now.AddDays(-1),
                now.AddDays(30),
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

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.VisitSchedulerWork
            .SingleAsync(x => x.Id == work.Id, cancellationToken);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, persisted.Status);
        Assert.InRange(
            (persisted.DueAt - now.AddDays(14)).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Scheduled_successor_releases_work_until_its_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"scheduled-wake-{suffix}", $"SCHEDULED-WAKE-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SW-{suffix[..2]}-{suffix[2..4]}", $"SW{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddMinutes(10);
        var endAt = now.AddHours(2);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddHours(-1),
            now.AddHours(3),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();

        var remote = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.NormalizedLicensePlate, startAt, endAt, "Oss"),
            cancellationToken);
        Assert.Equal("scheduled", remote.Status, ignoreCase: true);

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, startAt, endAt);
        action.MarkStarting();
        action.MarkScheduled(remote.ProviderActionId, remote.Status);

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now.AddMinutes(-1));
        work.Claim("scheduled-wake-test", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.VisitSchedulerWork.Add(work);
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
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.VisitSchedulerWork
            .SingleAsync(x => x.Id == work.Id, cancellationToken);
        Assert.Equal(VisitSchedulerWorkStatus.Pending, persisted.Status);
        Assert.InRange((persisted.DueAt - startAt).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.Null(persisted.ClaimedAt);
        Assert.Null(persisted.ClaimedBy);
    }

    [Fact]
    public async Task Claimed_scheduler_work_is_cancelled_when_visit_requires_attention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"attention-{suffix}", $"ATTENTION-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"AT-{suffix}", $"AT{suffix}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        visit.SetHealth(VisitHealth.AttentionRequired);
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(-1));
        work.Claim("attention-worker", now);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
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
            var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled,
            (await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken)).Status);
        Assert.Empty(await verifyContext.ProviderOperations.Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Continuation_start_preparation_replays_without_creating_another_action()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"next-action-{suffix}", $"NEXT-ACTION-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"NA-{suffix[..2]}-{suffix[2..4]}", $"NA{suffix[..4]}", null);
        var boundary = DateTimeOffset.UtcNow.AddMinutes(-1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            boundary.AddHours(-4), boundary.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var previous = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, visit.StartAt, boundary);
        previous.MarkStarting();
        previous.MarkActive($"provider-{suffix}", visit.StartAt);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(previous);
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
        var requestedEnd = boundary.AddHours(2);

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartStore>();
            var first = await store.PrepareAttemptAsync(visit, previous, operationId, requestedEnd, cancellationToken);
            Assert.False(first.IsReplay);
            Assert.True(first.AttemptStartedNow);
            Assert.Equal(ProviderOperationType.ContinueStart, first.Operation.Type);
            var guard = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartMutationGuard>();
            Assert.True(await guard.CanStartAsync(visit.Id, cancellationToken));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartStore>();
            var replay = await store.PrepareAttemptAsync(visit, previous, operationId, requestedEnd, cancellationToken);
            Assert.True(replay.IsReplay);
            Assert.False(replay.AttemptStartedNow);
            Assert.Equal(ProviderOperationStatus.InProgress, replay.Operation.Status);
            var results = scope.ServiceProvider.GetRequiredService<IProviderContinuationStartResultStore>();
            await results.RecordConfirmedAsync(replay,
                new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    $"provider-next-{suffix}", vehicle.NormalizedLicensePlate,
                    boundary.AddSeconds(1), requestedEnd, "Oss", "active"), cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        Assert.Equal(2, await verifyContext.ProviderParkingActions.CountAsync(x => x.VisitId == visit.Id, cancellationToken));
        var operation = Assert.Single(await verifyContext.ProviderOperations.Where(x => x.VisitId == visit.Id)
            .ToListAsync(cancellationToken));
        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.Equal(VisitStatus.Active,
            (await verifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken)).Status);
        Assert.Equal(ProviderActionState.Active,
            (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == operation.ProviderParkingActionId, cancellationToken)).State);
        Assert.Equal(ProviderActionState.Active,
            (await verifyContext.ProviderParkingActions.SingleAsync(x => x.Id == previous.Id, cancellationToken)).State);

        await using (var stopScope = provider.CreateAsyncScope())
        {
            var stopClaim = await stopScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), cancellationToken);
            var stopPreparation = await stopScope.ServiceProvider.GetRequiredService<IProviderStopStore>()
                .PrepareAttemptAsync(stopClaim, cancellationToken);
            Assert.Equal(operation.ProviderParkingActionId, stopPreparation.Action.Id);
        }
    }

    [Fact]
    public async Task Provider_extend_store_replays_same_operation_without_duplicate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "extend-store", "EXTEND-STORE", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "EX-11-HD", "EX11HD", null);
        var now = DateTimeOffset.UtcNow;
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, now.AddHours(-1), now.AddHours(2), snapshot);
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive("provider-extend", now.AddHours(-1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
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
        var providerEndAt = now.AddHours(1);

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProviderExtendStore>();
            var first = await store.PrepareAttemptAsync(visit, action, operationId, providerEndAt, cancellationToken);
            Assert.False(first.IsReplay);
            Assert.True(first.AttemptStartedNow);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProviderExtendStore>();
            var replay = await store.PrepareAttemptAsync(visit, action, operationId, providerEndAt, cancellationToken);
            Assert.True(replay.IsReplay);
            Assert.False(replay.AttemptStartedNow);
            Assert.Equal(operationId, replay.Operation.OperationId);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var operations = await verifyContext.ProviderOperations
            .Where(x => x.OperationId == operationId)
            .ToListAsync(cancellationToken);
        Assert.Single(operations);
        Assert.Equal(ProviderOperationStatus.InProgress, operations[0].Status);
        Assert.Equal(1, operations[0].AttemptCount);
    }

    [Fact]
    public async Task Successful_provider_extension_delivers_notification_to_active_admins_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-success-{suffix}", $"EXTEND-SUCCESS-{suffix}", "hash", UserRole.Visitor);
        var activeAdmin = new User(Guid.NewGuid(), $"extend-success-admin-{suffix}", $"EXTEND-SUCCESS-ADMIN-{suffix}", "hash", UserRole.Admin);
        var inactiveAdmin = new User(Guid.NewGuid(), $"extend-success-inactive-{suffix}", $"EXTEND-SUCCESS-INACTIVE-{suffix}", "hash", UserRole.Admin);
        inactiveAdmin.Deactivate();
        var vehicle = new Vehicle(Guid.NewGuid(), $"ES-{suffix[..2]}-{suffix[2..4]}", $"ES{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var extendedEnd = now.AddHours(1);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", now.AddHours(-1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.AddRange(user, activeAdmin, inactiveAdmin);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), now.AddDays(-1), null, TimeSpan.FromHours(4),
                Enumerable.Range(0, 7)
                    .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
                    .ToArray(),
                continuation: ProviderCoverageContinuation.ExtendAction));
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
            var preparation = await scope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
                .PrepareAttemptAsync(visit, action, Guid.NewGuid(), extendedEnd, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IProviderExtendResultStore>()
                .RecordConfirmedAsync(
                    preparation,
                    new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                        $"provider-{suffix}", vehicle.NormalizedLicensePlate,
                        now.AddHours(-1), extendedEnd, "Oss", "active"),
                    cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var notificationEvent = await verifyContext.NotificationEvents.SingleAsync(
            x => x.Type == NotificationEventType.ProviderContinuationSucceeded &&
                 x.AggregateId == visit.Id,
            cancellationToken);
        var notifications = await verifyContext.Notifications
            .Where(x => x.SourceEventId == notificationEvent.Id)
            .ToListAsync(cancellationToken);

        Assert.Contains(notifications, x =>
            x.RecipientUserId == activeAdmin.Id &&
            x.Type == NotificationType.ProviderContinuationSucceeded &&
            x.VisitId == visit.Id);
        Assert.DoesNotContain(notifications, x => x.RecipientUserId == user.Id);
        Assert.DoesNotContain(notifications, x => x.RecipientUserId == inactiveAdmin.Id);
        Assert.All(notifications, x =>
            Assert.Equal(NotificationType.ProviderContinuationSucceeded, x.Type));
    }

    [Fact]
    public async Task Unknown_provider_extension_delivers_attention_notification_to_visitor_and_active_admins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-unknown-{suffix}", $"EXTEND-UNKNOWN-{suffix}", "hash", UserRole.Visitor);
        var activeAdmin = new User(Guid.NewGuid(), $"extend-unknown-admin-{suffix}", $"EXTEND-UNKNOWN-ADMIN-{suffix}", "hash", UserRole.Admin);
        var inactiveAdmin = new User(Guid.NewGuid(), $"extend-unknown-inactive-{suffix}", $"EXTEND-UNKNOWN-INACTIVE-{suffix}", "hash", UserRole.Admin);
        inactiveAdmin.Deactivate();
        var vehicle = new Vehicle(Guid.NewGuid(), $"EU-{suffix[..2]}-{suffix[2..4]}", $"EU{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", now.AddHours(-1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.AddRange(user, activeAdmin, inactiveAdmin);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
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
            var preparation = await scope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
                .PrepareAttemptAsync(visit, action, Guid.NewGuid(), now.AddHours(1), cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IProviderExtendResultStore>()
                .RecordUnknownAsync(preparation, "provider-timeout", cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var notificationEvent = await verifyContext.NotificationEvents.SingleAsync(
            x => x.Type == NotificationEventType.ProviderContinuationAttentionRequired &&
                 x.AggregateId == visit.Id,
            cancellationToken);
        var notifications = await verifyContext.Notifications
            .Where(x => x.SourceEventId == notificationEvent.Id)
            .ToListAsync(cancellationToken);

        Assert.Contains(notifications, x =>
            x.RecipientUserId == user.Id &&
            x.Type == NotificationType.ProviderContinuationAttentionRequired &&
            x.VisitId == visit.Id);
        Assert.Contains(notifications, x =>
            x.RecipientUserId == activeAdmin.Id &&
            x.Type == NotificationType.ProviderContinuationAttentionRequired &&
            x.VisitId == visit.Id);
        Assert.DoesNotContain(notifications, x => x.RecipientUserId == inactiveAdmin.Id);
        Assert.All(notifications, x =>
            Assert.Equal(NotificationType.ProviderContinuationAttentionRequired, x.Type));
    }

    [Fact]
    public async Task Failed_provider_extension_delivers_attention_notification_to_visitor_and_active_admins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-failure-{suffix}", $"EXTEND-FAILURE-{suffix}", "hash", UserRole.Visitor);
        var activeAdmin = new User(Guid.NewGuid(), $"extend-admin-{suffix}", $"EXTEND-ADMIN-{suffix}", "hash", UserRole.Admin);
        var inactiveAdmin = new User(Guid.NewGuid(), $"extend-inactive-{suffix}", $"EXTEND-INACTIVE-{suffix}", "hash", UserRole.Admin);
        inactiveAdmin.Deactivate();
        var vehicle = new Vehicle(Guid.NewGuid(), $"EF-{suffix[..2]}-{suffix[2..4]}", $"EF{suffix[..4]}", null);
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", now.AddHours(-1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.AddRange(user, activeAdmin, inactiveAdmin);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
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
            var preparation = await scope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
                .PrepareAttemptAsync(visit, action, Guid.NewGuid(), now.AddHours(1), cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IProviderExtendResultStore>()
                .RecordDefinitiveFailureAsync(preparation, "provider-failure", cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var notificationEvent = await verifyContext.NotificationEvents.SingleAsync(
            x => x.Type == NotificationEventType.ProviderContinuationAttentionRequired &&
                 x.AggregateId == visit.Id,
            cancellationToken);
        var notifications = await verifyContext.Notifications
            .Where(x => x.SourceEventId == notificationEvent.Id)
            .ToListAsync(cancellationToken);

        Assert.Contains(notifications, x =>
            x.RecipientUserId == user.Id &&
            x.Type == NotificationType.ProviderContinuationAttentionRequired &&
            x.VisitId == visit.Id);
        Assert.Contains(notifications, x =>
            x.RecipientUserId == activeAdmin.Id &&
            x.Type == NotificationType.ProviderContinuationAttentionRequired &&
            x.VisitId == visit.Id);
        Assert.DoesNotContain(notifications, x => x.RecipientUserId == inactiveAdmin.Id);
        Assert.All(notifications, x =>
            Assert.Equal(NotificationType.ProviderContinuationAttentionRequired, x.Type));
    }

    [Fact]
    public async Task Confirmed_extension_schedules_followup_at_next_paid_window()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-gap-{suffix}", $"EXTEND-GAP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"EG-{suffix}", $"EG{suffix}".ToUpperInvariant(), null);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-1);
        var extendedEnd = now.AddHours(1);
        var nextPaidStart = now.AddHours(2);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            start, now.AddHours(3), new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", start);
        var paidWindows = Enumerable.Range(0, 7)
            .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
            .ToArray();

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(action);
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), start.AddDays(-1), extendedEnd, TimeSpan.FromHours(4), paidWindows,
                continuation: ProviderCoverageContinuation.ExtendAction));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), extendedEnd, nextPaidStart, TimeSpan.FromHours(4), Array.Empty<PaidWindow>()));
            seedContext.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(), nextPaidStart, null, TimeSpan.FromHours(4), paidWindows));
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
            var preparation = await scope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
                .PrepareAttemptAsync(visit, action, Guid.NewGuid(), extendedEnd, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IProviderExtendResultStore>()
                .RecordConfirmedAsync(preparation, new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                    $"provider-{suffix}", vehicle.NormalizedLicensePlate, start, extendedEnd, "Oss", "active"),
                    cancellationToken);
        }

        await using var verifyContext = fixture.CreateDbContext();
        var work = Assert.Single(await verifyContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id).ToListAsync(cancellationToken));
        Assert.InRange((work.DueAt - nextPaidStart).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Scheduler_work_claim_cancels_due_item_when_visit_is_stopping()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(cancellationToken);

        var user = new User(Guid.NewGuid(), "scheduler-stop", "SCHEDULER-STOP", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "SC-22-HD", "SC22HD", null);
        var now = DateTimeOffset.UtcNow;
        var snapshot = new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id, now.AddHours(-1), now.AddHours(2), snapshot);
        visit.Activate();
        visit.BeginStopping();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now.AddMinutes(-1));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.VisitSchedulerWork.Add(work);
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
        var claimer = scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();

        var claim = await claimer.ClaimNextDueAsync("worker-stop-test", now, cancellationToken);

        Assert.Null(claim);

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
        Assert.Equal(VisitSchedulerWorkStatus.Cancelled, persisted.Status);
        Assert.Null(persisted.ClaimedAt);
        Assert.Null(persisted.ClaimedBy);
    }

    private async Task ClearVisitsAsync(CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(cancellationToken);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderOperations.ExecuteDeleteAsync(cancellationToken);
        await context.ProviderParkingActions.ExecuteDeleteAsync(cancellationToken);
        await context.Notifications.ExecuteDeleteAsync(cancellationToken);
        await context.NotificationEvents.ExecuteDeleteAsync(cancellationToken);
        await context.Visits.ExecuteDeleteAsync(cancellationToken);
        await context.PaidWindows.ExecuteDeleteAsync(cancellationToken);
        await context.ParkingCalendarExceptions.ExecuteDeleteAsync(cancellationToken);
        await context.ParkingRuleSets.ExecuteDeleteAsync(cancellationToken);
    }

}
