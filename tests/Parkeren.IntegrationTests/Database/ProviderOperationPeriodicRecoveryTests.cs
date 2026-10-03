using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Infrastructure.Persistence;
using DomainProviderParkingAction = Parkeren.Domain.Visits.ProviderParkingAction;
using ApplicationProviderParkingAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderOperationPeriodicRecoveryTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Periodic_recovery_marks_only_expired_attempts_unknown(bool expired)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"recovery-{suffix}", $"RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"RC{suffix[..6]}", $"RC{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now, now.AddHours(1), new EffectiveParkingPolicySnapshot(null, null, true));
        var action = new DomainProviderParkingAction(Guid.NewGuid(), visit.Id, now, now.AddHours(1));
        action.MarkStarting();
        var operation = new ProviderOperation(
            Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.ProviderOperations.Add(operation);
            seed.Entry(operation).Property(x => x.AttemptStartedAt).CurrentValue = expired
                ? now.Subtract(ProviderOperationStartupRecovery.AttemptLease).AddSeconds(-1)
                : now;
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .RecoverExpiredInProgressOperationsAsync(cancellationToken);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedOperation = await verify.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken);
            var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);

            Assert.Equal(
                expired ? ProviderOperationStatus.Unknown : ProviderOperationStatus.InProgress,
                persistedOperation.Status);
            Assert.Equal(
                expired ? ProviderActionHealth.Unknown : ProviderActionHealth.Healthy,
                persistedAction.Health);
            Assert.Equal(
                expired ? VisitHealth.Reconciling : VisitHealth.Healthy,
                persistedVisit.Health);
        }

        await ClearVisitStateAsync(cancellationToken);
    }

    [Fact]
    public async Task Periodic_recovery_does_not_take_over_an_expired_attempt_active_in_this_process()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"active-recovery-{suffix}", $"ACTIVE-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"AR{suffix[..6]}", $"AR{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now, now.AddHours(1), new EffectiveParkingPolicySnapshot(null, null, true));
        var action = new DomainProviderParkingAction(Guid.NewGuid(), visit.Id, now, now.AddHours(1));
        action.MarkStarting();
        var operation = new ProviderOperation(
            Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.ProviderOperations.Add(operation);
            seed.Entry(operation).Property(x => x.AttemptStartedAt).CurrentValue =
                now.Subtract(ProviderOperationStartupRecovery.AttemptLease).AddSeconds(-1);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var provider = BuildServices();
        var tracker = provider.GetRequiredService<IProviderOperationExecutionTracker>();
        await using var scope = provider.CreateAsyncScope();
        var recovery = scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>();

        var activeLease = tracker.TryTrack(operation.OperationId);
        Assert.NotNull(activeLease);
        using (activeLease)
        {
            await recovery.RecoverExpiredInProgressOperationsAsync(cancellationToken);

            await using var activeCheck = fixture.CreateDbContext();
            Assert.Equal(ProviderOperationStatus.InProgress,
                (await activeCheck.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken)).Status);
        }

        await recovery.RecoverExpiredInProgressOperationsAsync(cancellationToken);

        await using (var verify = fixture.CreateDbContext())
        {
            Assert.Equal(ProviderOperationStatus.Unknown,
                (await verify.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken)).Status);
        }

        await ClearVisitStateAsync(cancellationToken);
    }

    [Fact]
    public async Task Startup_recovery_persists_existing_provider_start_after_lease_expiry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);
        var (user, vehicle, visit, action, operation, startAt, endAt) =
            await SeedStartAttemptAsync(cancellationToken);
        await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.LicensePlate, startAt, endAt, "Oss"),
            cancellationToken);

        await using var services = BuildServices(parkingProvider);
        await RunStartupRecoveryRoundsAsync(services, cancellationToken);

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
        var persistedOperation = await verify.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken);

        Assert.Equal(VisitStatus.Active, persistedVisit.Status);
        Assert.Equal(VisitHealth.Healthy, persistedVisit.Health);
        Assert.Equal(ProviderActionState.Active, persistedAction.State);
        Assert.Equal(ProviderActionHealth.Healthy, persistedAction.Health);
        Assert.NotNull(persistedAction.ProviderActionId);
        Assert.Equal(ProviderOperationStatus.Succeeded, persistedOperation.Status);
        await ClearVisitStateAsync(cancellationToken);
    }

    [Fact]
    public async Task Startup_before_lease_then_periodic_recovery_retries_only_after_provider_absence_readback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new CountingStartProvider(new TwoParkMockProvider(http));
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (user, vehicle, visit, action, operation, _, _) =
            await SeedStartAttemptAsync(cancellationToken, expired: false);

        await using var services = BuildServices(parkingProvider, clock);
        await using (var startupScope = services.CreateAsyncScope())
        {
            await startupScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .RecoverAsync(cancellationToken);
        }

        Assert.Equal(0, parkingProvider.StartCalls);
        Assert.Equal(0, parkingProvider.ReadBackCalls);

        var expiredAt = DateTimeOffset.UtcNow
            .Subtract(ProviderOperationStartupRecovery.AttemptLease)
            .AddSeconds(-1);
        await using (var ageAttempt = fixture.CreateDbContext())
        {
            await ageAttempt.ProviderOperations
                .Where(x => x.Id == operation.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AttemptStartedAt, expiredAt),
                    cancellationToken);
        }

        await using (var periodicScope = services.CreateAsyncScope())
        {
            var recovery = periodicScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>();
            await recovery.RecoverExpiredInProgressOperationsAsync(cancellationToken);
            await recovery.ReconcileUnknownOperationsAsync(cancellationToken);
            await periodicScope.ServiceProvider.GetRequiredService<IVisitTerminalRecoveryService>()
                .RecoverAsync(cancellationToken);
        }

        await using (var scheduled = fixture.CreateDbContext())
        {
            var stopWork = await scheduled.VisitSchedulerWork.SingleAsync(
                x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.StopVisit,
                cancellationToken);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, stopWork.Status);
            Assert.InRange(
                (stopWork.DueAt - visit.DesiredEndAt!.Value).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
            Assert.Equal(VisitEndReason.DesiredEndReached, stopWork.EndReason);
        }

        clock.Set(visit.DesiredEndAt!.Value);
        await using (var stopScope = services.CreateAsyncScope())
        {
            var claimer = stopScope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>();
            var stopWork = await claimer.ClaimNextDueAsync(
                "periodic-start-recovery-test",
                clock.GetUtcNow(),
                cancellationToken);
            Assert.NotNull(stopWork);
            await stopScope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(stopWork, cancellationToken);
        }

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
        var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
        var persistedOperation = await verify.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken);
        var persistedStopWork = await verify.VisitSchedulerWork.SingleAsync(
            x => x.VisitId == visit.Id && x.Type == VisitSchedulerWorkType.StopVisit,
            cancellationToken);

        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.Equal(VisitEndReason.DesiredEndReached, persistedVisit.EndReason);
        Assert.InRange(
            (persistedVisit.ActualEndAt!.Value - visit.DesiredEndAt.Value).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(VisitHealth.Healthy, persistedVisit.Health);
        Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        Assert.Equal(ProviderActionHealth.Healthy, persistedAction.Health);
        Assert.Equal(ProviderOperationStatus.Succeeded, persistedOperation.Status);
        Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedStopWork.Status);
        Assert.Equal(1, parkingProvider.StartCalls);
        Assert.True(parkingProvider.AbsenceConfirmedBeforeStart);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
        await ClearVisitStateAsync(cancellationToken);
    }

    private async Task<(User User, Vehicle Vehicle, Visit Visit, DomainProviderParkingAction Action,
        ProviderOperation Operation, DateTimeOffset StartAt, DateTimeOffset EndAt)> SeedStartAttemptAsync(
        CancellationToken cancellationToken,
        bool expired = true)
    {
        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddMinutes(-10);
        var endAt = now.AddHours(1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"start-recovery-{suffix}", $"START-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"SR{suffix[..6]}", $"SR{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, endAt, new EffectiveParkingPolicySnapshot(null, null, true),
            providerLocation: "Oss");
        var action = new DomainProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, endAt);
        action.MarkStarting();
        var operation = new ProviderOperation(
            Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        await using var seed = fixture.CreateDbContext();
        seed.Users.Add(user);
        seed.Vehicles.Add(vehicle);
        seed.Visits.Add(visit);
        seed.ProviderParkingActions.Add(action);
        seed.ProviderOperations.Add(operation);
        seed.Entry(operation).Property(x => x.AttemptStartedAt).CurrentValue =
            expired
                ? now.Subtract(ProviderOperationStartupRecovery.AttemptLease).AddSeconds(-1)
                : now;
        await seed.SaveChangesAsync(cancellationToken);
        return (user, vehicle, visit, action, operation, startAt, endAt);
    }

    private static async Task RunStartupRecoveryRoundsAsync(
        ServiceProvider services,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .RecoverAsync(cancellationToken);
        }
    }

    private ServiceProvider BuildServices(
        IParkingProvider? parkingProvider = null,
        TimeProvider? timeProvider = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
            ["ParkingProvider:Type"] = "TwoParkMock",
            ["ParkingProvider:BaseUrl"] = "http://localhost/"
        });

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        if (parkingProvider is not null)
            services.AddSingleton(parkingProvider);
        if (timeProvider is not null)
            services.AddSingleton(timeProvider);
        return services.BuildServiceProvider();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Set(DateTimeOffset value) => current = value;
    }

    private sealed class CountingStartProvider(IParkingProvider inner) : IParkingProvider
    {
        private int lastReadBackCount = -1;

        public int StartCalls { get; private set; }
        public int ReadBackCalls { get; private set; }
        public bool AbsenceConfirmedBeforeStart { get; private set; }

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            inner.GetCategoriesAsync(cancellationToken);

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            inner.GetProductAsync(cancellationToken);

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            inner.GetBalanceAsync(cancellationToken);

        public async Task<IReadOnlyList<ApplicationProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default)
        {
            ReadBackCalls++;
            var actions = await inner.GetActionsAsync(cancellationToken);
            lastReadBackCount = actions.Count;
            return actions;
        }

        public async Task<ApplicationProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            AbsenceConfirmedBeforeStart = ReadBackCalls > 0 && lastReadBackCount == 0;
            return await inner.StartActionAsync(request, cancellationToken);
        }

        public Task<ApplicationProviderParkingAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default) =>
            inner.ExtendActionAsync(providerActionId, newEnd, cancellationToken);

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            inner.StopActionAsync(providerActionId, cancellationToken);
    }

    private async Task ClearVisitStateAsync(CancellationToken cancellationToken)
    {
        await using var cleanup = fixture.CreateDbContext();
        await cleanup.VisitSchedulerWork.ExecuteDeleteAsync(cancellationToken);
        await cleanup.VisitEndTimeChanges.ExecuteDeleteAsync(cancellationToken);
        await cleanup.ProviderOperations.ExecuteDeleteAsync(cancellationToken);
        await cleanup.ProviderParkingActions.ExecuteDeleteAsync(cancellationToken);
        await cleanup.Notifications.ExecuteDeleteAsync(cancellationToken);
        await cleanup.NotificationEvents.ExecuteDeleteAsync(cancellationToken);
        await cleanup.Visits.ExecuteDeleteAsync(cancellationToken);
    }
}