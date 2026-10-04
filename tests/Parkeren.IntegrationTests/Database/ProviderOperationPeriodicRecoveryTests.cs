using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public async Task Scheduled_cancel_confirmation_uses_readback_start_for_action_accounting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var visitStart = now.AddHours(-1);
        var scheduledStart = now.AddMinutes(-10);
        var scheduledEnd = now.AddHours(1);
        var requestedEnd = now.AddMinutes(-15);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"cancel-{suffix}", $"CANCEL-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"CA{suffix[..6]}", $"CA{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            visitStart, now.AddHours(2), new EffectiveParkingPolicySnapshot(null, null, true));
        visit.Activate();
        var action = new DomainProviderParkingAction(Guid.NewGuid(), visit.Id, scheduledStart, scheduledEnd);
        action.MarkStarting();
        action.MarkScheduled("provider-scheduled-cancel", "scheduled");
        var operationId = Guid.NewGuid();
        var change = new VisitEndTimeChange(
            Guid.NewGuid(), operationId, visit.Id, user.Id,
            visit.DesiredEndAt, requestedEnd, now);
        var parkingProvider = new ScheduledCancelProvider(new ApplicationProviderParkingAction(
            "provider-scheduled-cancel", vehicle.NormalizedLicensePlate,
            scheduledStart, scheduledEnd, "Oss", "scheduled"));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitEndTimeChanges.Add(change);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var services = BuildServices(parkingProvider, new ManualTimeProvider(now));
        await using (var scope = services.CreateAsyncScope())
        {
            var adjustment = await scope.ServiceProvider.GetRequiredService<IVisitEndTimeProviderAdjuster>()
                .AdjustAsync(new ChangeVisitEndTimeCommand(operationId, visit.Id, user.Id, requestedEnd), cancellationToken);

            Assert.False(adjustment.RequiresReconciliation);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
            var stopOperation = await verify.ProviderOperations.SingleAsync(
                x => x.ProviderParkingActionId == action.Id && x.Type == ProviderOperationType.Stop,
                cancellationToken);

            Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
            Assert.InRange(
                (persistedAction.ActualStartAt!.Value - scheduledStart).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
            Assert.InRange(
                (persistedAction.ActualEndAt!.Value - now).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
            Assert.Equal(ProviderOperationStatus.Succeeded, stopOperation.Status);
        }

        await ClearVisitStateAsync(cancellationToken);
    }

    [Theory]
    [InlineData(ProviderOperationType.Start)]
    [InlineData(ProviderOperationType.ContinueStart)]
    public async Task Startup_recovery_skips_stop_with_unresolved_start_and_keeps_other_work_claimable(
        ProviderOperationType unresolvedOperationType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var stoppingUser = new User(Guid.NewGuid(), $"stopping-{suffix}", $"STOPPING-{suffix}", "hash", UserRole.Visitor);
        var stoppingVehicle = new Vehicle(Guid.NewGuid(), $"ST{suffix[..6]}", $"ST{suffix[..6]}", null);
        var stoppingVisit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), stoppingUser.Id, stoppingVehicle.Id, stoppingUser.Id,
            now.AddHours(-1), now.AddHours(2), new EffectiveParkingPolicySnapshot(null, null, true));
        stoppingVisit.Activate();
        stoppingVisit.BeginStopping(VisitEndReason.ManualStop);

        var uncertainAction = new DomainProviderParkingAction(
            Guid.NewGuid(), stoppingVisit.Id, now.AddHours(-1), now.AddMinutes(15));
        uncertainAction.MarkStarting();
        uncertainAction.MarkUnknown();
        var uncertainOperation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), stoppingVisit.Id, uncertainAction.Id, unresolvedOperationType);
        uncertainOperation.BeginAttempt();
        uncertainOperation.MarkUnknown("provider-timeout");
        var pendingStop = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), stoppingVisit.Id, null, ProviderOperationType.Stop);

        var workUser = new User(Guid.NewGuid(), $"work-{suffix}", $"WORK-{suffix}", "hash", UserRole.Visitor);
        var workVehicle = new Vehicle(Guid.NewGuid(), $"WK{suffix[..6]}", $"WK{suffix[..6]}", null);
        var workVisit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), workUser.Id, workVehicle.Id, workUser.Id,
            now.AddHours(-1), now.AddHours(2), new EffectiveParkingPolicySnapshot(null, null, true));
        workVisit.Activate();
        var workAction = new DomainProviderParkingAction(
            Guid.NewGuid(), workVisit.Id, now.AddHours(-1), now.AddHours(3));
        workAction.MarkStarting();
        workAction.MarkActive($"work-provider-{suffix}", now.AddHours(-1));
        var remoteWorkAction = new ApplicationProviderParkingAction(
            workAction.ProviderActionId!, workVehicle.NormalizedLicensePlate,
            workAction.PlannedStartAt, workAction.PlannedEndAt, "Oss", "active");
        var work = new VisitSchedulerWork(
            Guid.NewGuid(), workVisit.Id, VisitSchedulerWorkType.LongVisitWarning, now.AddMinutes(-1));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(stoppingUser, workUser);
            seed.Vehicles.AddRange(stoppingVehicle, workVehicle);
            seed.Visits.AddRange(stoppingVisit, workVisit);
            seed.ProviderParkingActions.AddRange(uncertainAction, workAction);
            seed.ProviderOperations.AddRange(uncertainOperation, pendingStop);
            seed.VisitSchedulerWork.Add(work);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var services = BuildServices(new RecoveryActionProvider(remoteWorkAction));
        await using (var recoveryScope = services.CreateAsyncScope())
        {
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .RecoverAsync(cancellationToken);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            Assert.Equal(VisitStatus.Stopping,
                (await verify.Visits.SingleAsync(x => x.Id == stoppingVisit.Id, cancellationToken)).Status);
            Assert.Equal(VisitHealth.AttentionRequired,
                (await verify.Visits.SingleAsync(x => x.Id == stoppingVisit.Id, cancellationToken)).Health);
            Assert.Equal(ProviderOperationStatus.Pending,
                (await verify.ProviderOperations.SingleAsync(x => x.Id == pendingStop.Id, cancellationToken)).Status);
            Assert.Equal(ProviderOperationStatus.Unknown,
                (await verify.ProviderOperations.SingleAsync(x => x.Id == uncertainOperation.Id, cancellationToken)).Status);
            Assert.Equal(VisitStatus.Active,
                (await verify.Visits.SingleAsync(x => x.Id == workVisit.Id, cancellationToken)).Status);
            var recoveredWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, recoveredWork.Status);
            Assert.True(recoveredWork.DueAt <= now);
        }

        await using (var claimScope = services.CreateAsyncScope())
        {
            var claimed = await claimScope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("recovery-after-uncertain-start", now, cancellationToken);
            Assert.NotNull(claimed);
            Assert.Equal(work.Id, claimed.Id);
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
        var operationEvents = await verify.VisitSchedulerAuditEvents
            .Where(x => x.VisitId == visit.Id && x.SourceId == operation.Id)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.EventOrder)
            .ThenBy(x => x.Id)
            .Select(x => x.EventType)
            .ToListAsync(cancellationToken);
        Assert.True(operationEvents.Contains("provider_operation.outcome_unknown"),
            $"Provider-operation audit sequence: {string.Join(", ", operationEvents)}");
        Assert.Equal(1, operationEvents.Count(x => x == "provider_operation.reconciliation_started"));
        Assert.Equal(1, operationEvents.Count(x => x == "provider_operation.succeeded"));
        Assert.True(operationEvents.IndexOf("provider_operation.outcome_unknown") <
                    operationEvents.IndexOf("provider_operation.reconciliation_started"));
        Assert.True(operationEvents.IndexOf("provider_operation.reconciliation_started") <
                    operationEvents.IndexOf("provider_operation.succeeded"));
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
            clock.Set(DateTimeOffset.UtcNow);

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
            var scheduledVisit = await scheduled.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
            var scheduledOperation = await scheduled.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken);
            var scheduledAction = await scheduled.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
            var visitWork = await scheduled.VisitSchedulerWork
                .Where(x => x.VisitId == visit.Id)
                .ToListAsync(cancellationToken);
            Assert.True(visitWork.Any(x => x.Type == VisitSchedulerWorkType.StopVisit),
                $"Expected terminal work for Visit {scheduledVisit.Status}/{scheduledVisit.Health}, " +
                $"operation {scheduledOperation.Status}/{scheduledOperation.AttemptCount}, " +
                $"action {scheduledAction.State}/{scheduledAction.Health}, Start calls {parkingProvider.StartCalls}; work: " +
                string.Join(", ", visitWork.Select(x => $"{x.Type}:{x.Status}")));
            var stopWork = Assert.Single(visitWork, x => x.Type == VisitSchedulerWorkType.StopVisit);
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
        var operationEvents = await verify.VisitSchedulerAuditEvents
            .Where(x => x.VisitId == visit.Id && x.SourceId == operation.Id)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.EventOrder)
            .ThenBy(x => x.Id)
            .Select(x => new { x.EventType, x.AttemptNumber })
            .ToListAsync(cancellationToken);
        var unknownEvent = Assert.Single(operationEvents, x => x.EventType == "provider_operation.outcome_unknown");
        var reconciliationEvent = Assert.Single(operationEvents, x => x.EventType == "provider_operation.reconciliation_started");
        var retryReadyEvent = Assert.Single(operationEvents, x => x.EventType == "provider_operation.retry_ready");
        var retryAttemptEvent = Assert.Single(operationEvents, x => x.EventType == "provider_operation.attempt_started");
        var successEvent = Assert.Single(operationEvents, x => x.EventType == "provider_operation.succeeded");
        Assert.Equal(1, unknownEvent.AttemptNumber);
        Assert.Equal(1, reconciliationEvent.AttemptNumber);
        Assert.Equal(1, retryReadyEvent.AttemptNumber);
        Assert.Equal(2, retryAttemptEvent.AttemptNumber);
        Assert.Equal(2, successEvent.AttemptNumber);
        Assert.True(operationEvents.IndexOf(unknownEvent) < operationEvents.IndexOf(retryReadyEvent));
        Assert.True(operationEvents.IndexOf(unknownEvent) < operationEvents.IndexOf(reconciliationEvent));
        Assert.True(operationEvents.IndexOf(reconciliationEvent) < operationEvents.IndexOf(retryReadyEvent));
        Assert.True(operationEvents.IndexOf(retryReadyEvent) < operationEvents.IndexOf(retryAttemptEvent));
        Assert.True(operationEvents.IndexOf(retryAttemptEvent) < operationEvents.IndexOf(successEvent));
        Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedStopWork.Status);
        Assert.Equal(1, parkingProvider.StartCalls);
        Assert.True(parkingProvider.AbsenceConfirmedBeforeStart);
        Assert.Single(await parkingProvider.GetActionsAsync(cancellationToken));
        await ClearVisitStateAsync(cancellationToken);
    }

    [Fact]
    public async Task Retryable_start_can_be_retried_on_same_context_after_save_failure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(cancellationToken);
        var (_, _, visit, action, operation, _, _) = await SeedStartAttemptAsync(cancellationToken);

        await using (var markUnknown = fixture.CreateDbContext())
        {
            var persistedOperation = await markUnknown.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken);
            var persistedAction = await markUnknown.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken);
            persistedOperation.MarkUnknown("readback_absent");
            persistedAction.MarkUnknown();
            await markUnknown.SaveChangesAsync(cancellationToken);
        }

        ProviderStartPreparation preparation;
        await using (var load = fixture.CreateDbContext())
        {
            preparation = new ProviderStartPreparation(
                await load.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken),
                await load.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, cancellationToken),
                IsReplay: true);
        }

        await using (var services = BuildServices(saveChangesInterceptor: new FailFirstSaveChangesInterceptor()))
        await using (var scope = services.CreateAsyncScope())
        {
            var resultStore = scope.ServiceProvider.GetRequiredService<IProviderStartResultStore>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resultStore.RecordRetryableAsync(preparation, cancellationToken));
            await resultStore.RecordRetryableAsync(preparation, cancellationToken);
        }

        await using var verify = fixture.CreateDbContext();
        var retriedOperation = await verify.ProviderOperations.SingleAsync(x => x.Id == operation.Id, cancellationToken);
        Assert.Equal(ProviderOperationStatus.Pending, retriedOperation.Status);
        var reconciliationEvent = await verify.VisitSchedulerAuditEvents
            .Where(x => x.VisitId == visit.Id && x.SourceId == operation.Id &&
                        x.EventType == "provider_operation.reconciliation_started")
            .ToListAsync(cancellationToken);
        Assert.Single(reconciliationEvent);
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
        TimeProvider? timeProvider = null,
        SaveChangesInterceptor? saveChangesInterceptor = null)
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
        if (saveChangesInterceptor is not null)
            services.AddDbContext<ParkerenDbContext>(options => options.AddInterceptors(saveChangesInterceptor));
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

    private sealed class FailFirstSaveChangesInterceptor : SaveChangesInterceptor
    {
        private bool failNextSave = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (failNextSave)
            {
                failNextSave = false;
                throw new InvalidOperationException("Injected SaveChanges failure.");
            }

            return ValueTask.FromResult(result);
        }
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

    private sealed class RecoveryActionProvider(ApplicationProviderParkingAction action) : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ApplicationProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationProviderParkingAction>>([action]);

        public Task<ApplicationProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApplicationProviderParkingAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ScheduledCancelProvider(ApplicationProviderParkingAction action) : IParkingProvider
    {
        private ApplicationProviderParkingAction current = action;

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ApplicationProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationProviderParkingAction>>([current]);

        public Task<ApplicationProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApplicationProviderParkingAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
        {
            current = current with { Status = "stopped" };
            return Task.CompletedTask;
        }
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