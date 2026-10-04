using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class VisitSchedulerLockingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Scheduler_work_transitions_are_persisted_and_grouped_by_attempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);

        var services = CreateServices();
        await using (var provider = services.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
            var claimed = await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("scheduler-audit-test", now, ct);

            Assert.NotNull(claimed);
            claimed!.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(ct);
        }

        await using var verify = fixture.CreateDbContext();
        var events = await verify.VisitSchedulerAuditEvents
            .Where(x => x.VisitId == visit.Id && x.SourceType == "scheduler_work")
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        Assert.Equal(3, events.Count);
        Assert.Equal("scheduler_work.created", events[0].EventType);
        Assert.Equal("scheduler_work.claimed", events[1].EventType);
        Assert.Equal("scheduler_work.released", events[2].EventType);
        Assert.Equal($"attempt:{work.Id}:1", events[1].GroupKey);
        Assert.Equal(events[1].GroupKey, events[2].GroupKey);
        Assert.Equal(1, events[2].AttemptNumber);
        Assert.Equal(events.Count, events.Select(x => x.EventKey).Distinct().Count());
    }

    [Fact]
    public async Task Cancelled_scheduler_audit_save_can_be_retried_on_the_same_context()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);

        await using var dbContext = fixture.CreateDbContext();
        var trackedWork = await dbContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
        trackedWork.Claim("scheduler-retry-test", now);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dbContext.SaveChangesAsync(cancelled.Token));

        await dbContext.SaveChangesAsync(ct);

        await using var verify = fixture.CreateDbContext();
        var claimEvents = await verify.VisitSchedulerAuditEvents
            .Where(x => x.VisitId == visit.Id && x.EventType == "scheduler_work.claimed")
            .ToListAsync(ct);
        Assert.Single(claimEvents);
    }

    [Fact]
    public async Task Provider_operation_attempt_and_success_in_one_save_are_both_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, _) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);
        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, ProviderOperationType.Stop);

        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.ProviderOperations.Add(operation);
            await dbContext.SaveChangesAsync(ct);

            operation.BeginAttempt();
            operation.Succeed(now);
            await dbContext.SaveChangesAsync(ct);
        }

        await using var verify = fixture.CreateDbContext();
        var events = await verify.VisitSchedulerAuditEvents
            .Where(x => x.VisitId == visit.Id && x.SourceId == operation.Id)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var attemptEvent = Assert.Single(events, x => x.EventType == "provider_operation.attempt_started");
        var successEvent = Assert.Single(events, x => x.EventType == "provider_operation.succeeded");
        Assert.Equal(attemptEvent.GroupKey, successEvent.GroupKey);
        Assert.Equal(attemptEvent.OccurredAt, successEvent.OccurredAt);
        Assert.True(attemptEvent.EventOrder < successEvent.EventOrder);
    }

    [Fact]
    public async Task Two_workers_cannot_claim_the_same_scheduler_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task<VisitSchedulerWork?> ClaimAsync(string workerId)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync(workerId, now, ct);
        }

        var results = await Task.WhenAll(
            ClaimAsync("scheduler-worker-a"),
            ClaimAsync("scheduler-worker-b"));

        Assert.Single(results, x => x is not null);

        await using var verify = fixture.CreateDbContext();
        var persisted = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
        Assert.Equal(VisitSchedulerWorkStatus.Claimed, persisted.Status);
        Assert.Contains(persisted.ClaimedBy, new[] { "scheduler-worker-a", "scheduler-worker-b" });
        Assert.Equal(visit.Id, persisted.VisitId);
    }

    [Fact]
    public async Task Stop_visit_has_priority_when_due_time_matches_other_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, _) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.LongVisitWarning,
            now,
            ct);

        Guid stopWorkId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.VisitSchedulerWork.Add(new VisitSchedulerWork(
                Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, now));
            var stopWork = new VisitSchedulerWork(
                Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.StopVisit, now);
            stopWorkId = stopWork.Id;
            seed.VisitSchedulerWork.Add(stopWork);
            await seed.SaveChangesAsync(ct);
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var claimed = await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
            .ClaimNextDueAsync("scheduler-priority", now, ct);

        Assert.NotNull(claimed);
        Assert.Equal(stopWorkId, claimed!.Id);
        Assert.Equal(VisitSchedulerWorkType.StopVisit, claimed.Type);
    }

    [Fact]
    public async Task Scheduler_claim_and_manual_stop_complete_without_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task ClaimSchedulerAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("scheduler-race", now, ct);
        }

        async Task StopVisitAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, visit.UserId), ct);
        }

        await Task.WhenAll(ClaimSchedulerAsync(), StopVisitAsync()).WaitAsync(TimeSpan.FromSeconds(10), ct);

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Contains(persistedWork.Status, new[]
        {
            VisitSchedulerWorkStatus.Claimed,
            VisitSchedulerWorkStatus.Cancelled
        });
        Assert.Equal(1, await verify.ProviderOperations.CountAsync(
            x => x.VisitId == visit.Id && x.Type == ProviderOperationType.Stop,
            ct));
    }

    [Fact]
    public async Task Scheduler_claim_and_end_time_change_complete_without_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var (visit, work) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            now,
            ct);
        var requestedEndAt = visit.DesiredEndAt!.Value.AddMinutes(-15);
        var command = new ChangeVisitEndTimeCommand(
            Guid.NewGuid(), visit.Id, visit.UserId, requestedEndAt);

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task ClaimSchedulerAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkClaimer>()
                .ClaimNextDueAsync("scheduler-end-time-race", now, ct);
        }

        async Task<bool> ChangeEndTimeAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>()
                    .ApplyAsync(command, ct);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var claimTask = ClaimSchedulerAsync();
        var changeTask = ChangeEndTimeAsync();
        await Task.WhenAll(claimTask, changeTask).WaitAsync(TimeSpan.FromSeconds(10), ct);
        var endTimeApplied = await changeTask;

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
        var persistedChange = await verify.VisitEndTimeChanges.SingleAsync(x => x.OperationId == command.OperationId, ct);

        Assert.Equal(VisitSchedulerWorkStatus.Claimed, persistedWork.Status);
        if (endTimeApplied)
        {
            Assert.Equal(requestedEndAt, persistedVisit.DesiredEndAt);
            Assert.Equal(VisitEndTimeChangeResult.Applied, persistedChange.Result);
        }
        else
        {
            Assert.Equal(visit.DesiredEndAt, persistedVisit.DesiredEndAt);
            Assert.Equal(VisitEndTimeChangeResult.Rejected, persistedChange.Result);
        }
    }

    [Fact]
    public async Task Stop_and_end_time_change_are_serialized_without_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var (visit, _) = await SeedActiveVisitWithWorkAsync(
            now,
            VisitSchedulerWorkType.LongVisitWarning,
            now.AddHours(4),
            ct);
        var requestedEndAt = visit.DesiredEndAt!.Value.AddMinutes(-15);
        var operationId = Guid.NewGuid();
        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            now.AddDays(-1),
            requestedEndAt.AddDays(1),
            TimeSpan.FromHours(4),
            Array.Empty<PaidWindow>());

        await using (var seed = fixture.CreateDbContext())
        {
            seed.ParkingRuleSets.Add(rules);
            await seed.SaveChangesAsync(ct);
        }

        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();

        async Task<StopVisitClaim> ClaimStopAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, visit.UserId), ct);
        }

        async Task<bool> ChangeEndTimeAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IVisitEndTimeChanger>().ApplyAsync(
                    new ChangeVisitEndTimeCommand(operationId, visit.Id, visit.UserId, requestedEndAt),
                    ct);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var stopTask = ClaimStopAsync();
        var changeTask = ChangeEndTimeAsync();
        await Task.WhenAll(stopTask, changeTask).WaitAsync(TimeSpan.FromSeconds(10), ct);
        var stopClaim = await stopTask;
        Assert.NotNull(stopClaim.Operation);

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var stopOperation = await verify.ProviderOperations.SingleAsync(
            x => x.VisitId == visit.Id && x.Type == ProviderOperationType.Stop,
            ct);
        var change = await verify.VisitEndTimeChanges.SingleAsync(x => x.OperationId == operationId, ct);

        Assert.Equal(VisitStatus.Stopping, persistedVisit.Status);
        Assert.Equal(ProviderOperationStatus.Pending, stopOperation.Status);
        if (await changeTask)
        {
            Assert.Equal(VisitEndTimeChangeResult.Applied, change.Result);
            Assert.InRange(
                (persistedVisit.DesiredEndAt!.Value - requestedEndAt).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
        }
        else
        {
            Assert.Equal(VisitEndTimeChangeResult.Rejected, change.Result);
            Assert.InRange(
                (persistedVisit.DesiredEndAt!.Value - visit.DesiredEndAt!.Value).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
        }

        await verify.ParkingRuleSets
            .Where(x => x.Id == rules.Id)
            .ExecuteDeleteAsync(ct);
    }

    [Fact]
    public Task Extend_is_blocked_when_stop_starts_after_scheduler_load() =>
        AssertExtendBlockedAfterExternalChangeAsync(beginStop: true);

    [Fact]
    public Task Extend_is_blocked_when_health_changes_after_scheduler_load() =>
        AssertExtendBlockedAfterExternalChangeAsync(beginStop: false);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stop_reads_back_an_uncertain_extend_and_stops_the_observed_action(bool providerAppliedExtend)
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-stop-{suffix}", $"EXTEND-STOP-{suffix}", "hash", UserRole.Visitor);
        var plate = $"ES{suffix[..6]}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var now = DateTimeOffset.UtcNow;
        var requestedEnd = now.AddHours(1);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", now.AddHours(-1));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            await seed.SaveChangesAsync(ct);
        }

        var parkingProvider = new RecordingExtendProvider(
            action.ProviderActionId!, plate, action.PlannedStartAt, action.PlannedEndAt,
            blockExtend: true, throwAfterExtend: true, applyExtend: providerAppliedExtend);
        var services = CreateServices();
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();

        await using var extendScope = provider.CreateAsyncScope();
        var extendContext = extendScope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
        var trackedVisit = await extendContext.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var trackedAction = await extendContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
        var preparation = await extendScope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
            .PrepareAttemptAsync(trackedVisit, trackedAction, Guid.NewGuid(), requestedEnd, ct);
        var extendTask = extendScope.ServiceProvider.GetRequiredService<ContinueVisitProviderExecutor>()
            .ExecuteAsync(preparation, ct);
        await parkingProvider.ExtendStarted.Task.WaitAsync(ct);

        await using var stopScope = provider.CreateAsyncScope();
        var stopFlow = stopScope.ServiceProvider.GetRequiredService<StopVisitFlow>();
        var stopTask = stopFlow.StopAsync(
            new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id),
            new StopVisitContext(new StopVisitActor(user.Id, UserRole.Visitor, true), visit),
            ct);

        await WaitForVisitStatusAsync(visit.Id, VisitStatus.Stopping, ct);
        Assert.False(stopTask.IsCompleted);
        Assert.Equal(0, parkingProvider.StopCalls);

        parkingProvider.CompleteExtend();
        var extension = await extendTask;
        var stop = await stopTask.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.True(extension.RequiresReconciliation);
        Assert.Equal(StopVisitFlowOutcome.Completed, stop.Outcome);
        Assert.Equal(1, parkingProvider.ExtendCalls);
        Assert.Equal(1, parkingProvider.StopCalls);

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
        var extendOperation = await verify.ProviderOperations.SingleAsync(x => x.Type == ProviderOperationType.Extend, ct);
        var stopOperation = await verify.ProviderOperations.SingleAsync(x => x.Type == ProviderOperationType.Stop, ct);
        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        if (providerAppliedExtend)
        {
            Assert.InRange(
                (persistedAction.PlannedEndAt - requestedEnd).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
            Assert.Equal(ProviderOperationStatus.Succeeded, extendOperation.Status);
        }
        else
        {
            Assert.InRange(
                (persistedAction.PlannedEndAt - action.PlannedEndAt).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
            Assert.Equal(ProviderOperationStatus.Failed, extendOperation.Status);
        }
        Assert.Equal(ProviderOperationStatus.Succeeded, stopOperation.Status);
        await ClearVisitsAsync(ct);
    }

    [Fact]
    public Task Stop_recovers_after_request_cancellation_during_extend_wait() =>
        AssertPersistedStopRecoversAsync(cancelDuringWait: true, failFirstReadBack: false);

    [Fact]
    public Task Stop_recovers_after_temporary_extend_readback_failure() =>
        AssertPersistedStopRecoversAsync(cancelDuringWait: false, failFirstReadBack: true);

    private async Task AssertPersistedStopRecoversAsync(bool cancelDuringWait, bool failFirstReadBack)
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-recovery-{suffix}", $"EXTEND-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var plate = $"ER{suffix[..6]}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var now = DateTimeOffset.UtcNow;
        var requestedEnd = now.AddHours(1);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddHours(-1), now.AddHours(2),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddMinutes(5));
        action.MarkStarting();
        action.MarkActive($"provider-{suffix}", now.AddHours(-1));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            await seed.SaveChangesAsync(ct);
        }

        var parkingProvider = new RecordingExtendProvider(
            action.ProviderActionId!, plate, action.PlannedStartAt, action.PlannedEndAt,
            blockExtend: true, throwAfterExtend: true, failFirstPostExtendReadBack: failFirstReadBack);
        var services = CreateServices();
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();

        await using var extendScope = provider.CreateAsyncScope();
        var extendContext = extendScope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
        var trackedVisit = await extendContext.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var trackedAction = await extendContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
        var preparation = await extendScope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
            .PrepareAttemptAsync(trackedVisit, trackedAction, Guid.NewGuid(), requestedEnd, ct);
        var extendTask = extendScope.ServiceProvider.GetRequiredService<ContinueVisitProviderExecutor>()
            .ExecuteAsync(preparation, ct);
        await parkingProvider.ExtendStarted.Task.WaitAsync(ct);

        using var stopCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var stopScope = provider.CreateAsyncScope();
        var stopTask = stopScope.ServiceProvider.GetRequiredService<StopVisitFlow>()
            .StopAsync(
                new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id),
                new StopVisitContext(new StopVisitActor(user.Id, UserRole.Visitor, true), visit),
                stopCancellation.Token);
        await WaitForVisitStatusAsync(visit.Id, VisitStatus.Stopping, ct);

        if (cancelDuringWait)
        {
            stopCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopTask);
        }

        parkingProvider.CompleteExtend();
        var extension = await extendTask;
        Assert.True(extension.RequiresReconciliation);

        if (!cancelDuringWait)
        {
            var stop = await stopTask.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Equal(StopVisitFlowOutcome.ReconciliationRequired, stop.Outcome);
        }

        Assert.Equal(0, parkingProvider.StopCalls);
        await using (var pending = fixture.CreateDbContext())
        {
            Assert.Equal(VisitStatus.Stopping,
                (await pending.Visits.SingleAsync(x => x.Id == visit.Id, ct)).Status);
            Assert.Equal(ProviderOperationStatus.Unknown,
                (await pending.ProviderOperations.SingleAsync(x => x.Type == ProviderOperationType.Extend, ct)).Status);
            Assert.Equal(ProviderOperationStatus.Pending,
                (await pending.ProviderOperations.SingleAsync(x => x.Type == ProviderOperationType.Stop, ct)).Status);
        }

        await using (var recoveryScope = provider.CreateAsyncScope())
        {
            await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                .ReconcileUnknownOperationsAsync(ct);
        }

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
        var extendOperation = await verify.ProviderOperations.SingleAsync(x => x.Type == ProviderOperationType.Extend, ct);
        var stopOperation = await verify.ProviderOperations.SingleAsync(x => x.Type == ProviderOperationType.Stop, ct);
        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        Assert.Equal(ProviderOperationStatus.Succeeded, extendOperation.Status);
        Assert.Equal(ProviderOperationStatus.Succeeded, stopOperation.Status);
        Assert.Equal(1, parkingProvider.StopCalls);
        await ClearVisitsAsync(ct);
    }

    private async Task WaitForVisitStatusAsync(Guid visitId, VisitStatus expected, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            await using var context = fixture.CreateDbContext();
            if (await context.Visits.AsNoTracking().AnyAsync(x => x.Id == visitId && x.Status == expected, timeout.Token))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private async Task AssertExtendBlockedAfterExternalChangeAsync(bool beginStop)
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitsAsync(ct);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"extend-guard-{suffix}", $"EXTEND-GUARD-{suffix}", "hash", UserRole.Visitor);
        var plate = $"EG{suffix[..6]}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
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

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            await seed.SaveChangesAsync(ct);
        }

        var parkingProvider = new RecordingExtendProvider(
            action.ProviderActionId!, plate, action.PlannedStartAt, action.PlannedEndAt);
        var services = CreateServices();
        services.AddSingleton<IParkingProvider>(parkingProvider);
        await using var provider = services.BuildServiceProvider();
        await using var schedulerScope = provider.CreateAsyncScope();
        var schedulerContext = schedulerScope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
        var trackedVisit = await schedulerContext.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var trackedAction = await schedulerContext.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
        var preparation = await schedulerScope.ServiceProvider.GetRequiredService<IProviderExtendStore>()
            .PrepareAttemptAsync(trackedVisit, trackedAction, Guid.NewGuid(), now.AddHours(1), ct);

        StopVisitCommand? stopCommand = null;
        await using (var secondScope = provider.CreateAsyncScope())
        {
            if (beginStop)
            {
                stopCommand = new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id);
                await secondScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                    .ClaimAsync(stopCommand, ct);
            }
            else
            {
                var secondContext = secondScope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
                var changedVisit = await secondContext.Visits.SingleAsync(x => x.Id == visit.Id, ct);
                changedVisit.SetHealth(VisitHealth.Reconciling);
                await secondContext.SaveChangesAsync(ct);
            }
        }

        var execution = await schedulerScope.ServiceProvider.GetRequiredService<ContinueVisitProviderExecutor>()
            .ExecuteAsync(preparation, ct);

        Assert.False(execution.RequiresReconciliation);
        Assert.True(execution.DefinitiveFailure);
        Assert.Equal(0, parkingProvider.ExtendCalls);

        await using (var verifyBlocked = fixture.CreateDbContext())
        {
            var blockedOperation = await verifyBlocked.ProviderOperations.SingleAsync(
                x => x.Type == ProviderOperationType.Extend,
                ct);
            Assert.Equal(ProviderOperationStatus.Failed, blockedOperation.Status);
        }

        stopCommand ??= new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id);
        await using (var stopScope = provider.CreateAsyncScope())
        {
            var stopped = await stopScope.ServiceProvider.GetRequiredService<StopVisitFlow>()
                .StopAsync(
                    stopCommand,
                    new StopVisitContext(new StopVisitActor(user.Id, UserRole.Visitor, true), visit),
                    ct);
            Assert.Equal(StopVisitFlowOutcome.Completed, stopped.Outcome);
        }

        await using var verify = fixture.CreateDbContext();
        var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
        var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
        var persistedStop = await verify.ProviderOperations.SingleAsync(
            x => x.Type == ProviderOperationType.Stop,
            ct);
        Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
        Assert.Equal(ProviderActionState.Stopped, persistedAction.State);
        Assert.Equal(ProviderOperationStatus.Succeeded, persistedStop.Status);
        Assert.Equal(1, parkingProvider.StopCalls);
        await ClearVisitsAsync(ct);
    }

    private sealed class RecordingExtendProvider(
        string providerActionId,
        string licensePlate,
        DateTimeOffset start,
        DateTimeOffset end,
        bool blockExtend = false,
        bool throwAfterExtend = false,
        bool applyExtend = true,
        bool failFirstPostExtendReadBack = false) : IParkingProvider
    {
        private Parkeren.Application.ParkingProvider.ProviderParkingAction current = new(
            providerActionId, licensePlate, start, end, "Oss", "active");
        private readonly TaskCompletionSource extendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readBackFailed;

        public int ExtendCalls { get; private set; }
        public int StopCalls { get; private set; }
        public TaskCompletionSource ExtendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void CompleteExtend() => extendGate.TrySetResult();

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderCategory>>([]);

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderProduct("product", "test", "Oss"));

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderBalance(0, ProviderBalanceUnit.Euro, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(
            CancellationToken cancellationToken = default)
        {
            if (failFirstPostExtendReadBack && ExtendCalls > 0 && Interlocked.Exchange(ref readBackFailed, 1) == 0)
                throw new HttpRequestException("Temporary provider read-back failure.");

            return Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>([current]);
        }

        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(
            string actionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default)
        {
            ExtendCalls++;
            ExtendStarted.TrySetResult();
            if (blockExtend)
                await extendGate.Task.WaitAsync(cancellationToken);

            if (applyExtend)
                current = current with { End = newEnd };
            if (throwAfterExtend)
                throw new HttpRequestException("The provider response was lost after the mutation.");

            return current;
        }

        public Task StopActionAsync(string actionId, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            current = current with { Status = "stopped" };
            return Task.CompletedTask;
        }
    }

    private async Task<(Visit Visit, VisitSchedulerWork Work)> SeedActiveVisitWithWorkAsync(
        DateTimeOffset now,
        VisitSchedulerWorkType type,
        DateTimeOffset dueAt,
        CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            Guid.NewGuid(),
            $"scheduler-{suffix}",
            $"SCHEDULER-{suffix}",
            "hash",
            UserRole.Visitor);
        var plate = $"SC{suffix}".ToUpperInvariant();
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            now.AddMinutes(-30),
            now.AddHours(1),
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true));
        visit.Activate();
        var work = new VisitSchedulerWork(Guid.NewGuid(), visit.Id, type, dueAt);

        await using var seed = fixture.CreateDbContext();
        seed.Users.Add(user);
        seed.Vehicles.Add(vehicle);
        seed.Visits.Add(visit);
        seed.VisitSchedulerWork.Add(work);
        await seed.SaveChangesAsync(ct);
        return (visit, work);
    }

    private ServiceCollection CreateServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services;
    }

    private async Task ClearVisitsAsync(CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await context.ProviderOperations.ExecuteDeleteAsync(ct);
        await context.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await context.Notifications.ExecuteDeleteAsync(ct);
        await context.NotificationEvents.ExecuteDeleteAsync(ct);
        await context.DeleteVisitSchedulerAuditEventsAsync(ct);
        await context.Visits.ExecuteDeleteAsync(ct);
    }
}
