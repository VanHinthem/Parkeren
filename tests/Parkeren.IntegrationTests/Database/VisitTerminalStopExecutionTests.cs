using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class VisitTerminalStopExecutionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Scheduled_terminal_stop_persists_reason_and_functional_boundary()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddHours(-2);
        var boundary = now.AddMinutes(-5);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"terminal-stop-{suffix}", $"TERMINAL-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"TB{suffix[..6]}", $"TB{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            boundary,
            new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.StopVisit,
            boundary,
            VisitEndReason.DesiredEndReached);
        work.Claim("terminal-stop-test", now);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.VisitSchedulerWork.Add(work);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var claimed = await dbContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
            var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            var operation = await verify.ProviderOperations.SingleAsync(x => x.OperationId == work.Id, ct);

            Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
            Assert.Equal(VisitEndReason.DesiredEndReached, persistedVisit.EndReason);
            Assert.NotNull(persistedVisit.ActualEndAt);
            Assert.InRange(
                (persistedVisit.ActualEndAt.Value - now).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5));
            Assert.True(persistedVisit.ActualEndAt > boundary);
            Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedWork.Status);
            Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
            Assert.Equal(persistedVisit.ActualEndAt, operation.CompletedAt);
            Assert.False(await verify.ProviderParkingActions.AnyAsync(x => x.VisitId == visit.Id, ct));
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Stop_claim_preserves_pending_provider_action_reconciliation()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddMinutes(-20);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"history-stop-{suffix}", $"HISTORY-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"HS{suffix[..6]}", $"HS{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, now.AddHours(2), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, startAt, now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-action", startAt);
        var reconciliation = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ReconcileProviderAction,
            now.AddMinutes(1), providerParkingActionId: action.Id);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitSchedulerWork.Add(reconciliation);
            await seed.SaveChangesAsync(ct);
        }

        await using (var scope = BuildServices().CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == reconciliation.Id, ct);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, persistedWork.Status);
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Provider_history_reconciles_action_after_visit_is_completed()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var originalStart = now.AddMinutes(-25);
        var originalEnd = now.AddMinutes(-2);
        var correctedStart = now.AddMinutes(-30);
        var correctedEnd = now.AddMinutes(-3);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"history-reconcile-{suffix}", $"HISTORY-RECONCILE-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"HR{suffix[..6]}", $"HR{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            originalStart, now.AddHours(1), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(now);

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, originalStart, now.AddMinutes(30), "product-1");
        action.MarkStarting();
        action.MarkActive("provider-action-1", originalStart);
        action.BeginStopping();
        action.MarkStopped(originalEnd, "stopped");
        action.ScheduleHistoryReconciliation();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ReconcileProviderAction,
            now.AddMinutes(-1), providerParkingActionId: action.Id);
        work.Claim("history-reconcile-test", now);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitSchedulerWork.Add(work);
            await seed.SaveChangesAsync(ct);
        }

        var historyReader = new PagedHistoryReader(new ProviderActionHistoryRecord(
            "provider-action-1", "COMPLETED", correctedStart, correctedEnd, 0.42m, "EUR"));
        await using (var scope = BuildServices(historyReader).CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var claimed = await dbContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
            var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);

            Assert.Equal(VisitStatus.Completed, (await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct)).Status);
            Assert.Equal(ProviderHistoryStatus.Reconciled, persistedAction.HistoryStatus);
            Assert.InRange((correctedStart - persistedAction.ActualStartAt!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
            Assert.InRange((correctedEnd - persistedAction.ActualEndAt!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
            Assert.Equal(0.42m, persistedAction.ProviderCostAmount);
            Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedWork.Status);
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Provider_history_correction_evaluates_budget_warning_from_corrected_interval()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var correctedStart = new DateTimeOffset(2026, 9, 30, 7, 59, 0, TimeSpan.Zero);
        var visitStart = correctedStart.AddMinutes(2);
        var originalStart = visitStart.AddMinutes(2);
        var actionEnd = correctedStart.AddMinutes(11);
        var now = correctedStart.AddMinutes(12);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"history-budget-{suffix}", $"HISTORY-BUDGET-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"HB{suffix[..6]}", $"HB{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            visitStart, now.AddHours(1), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(now);

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, originalStart, now.AddMinutes(30), "product-1");
        action.MarkStarting();
        action.MarkActive("provider-action-budget", originalStart);
        action.BeginStopping();
        action.MarkStopped(actionEnd, "stopped");
        action.ScheduleHistoryReconciliation();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ReconcileProviderAction,
            now.AddMinutes(-1), providerParkingActionId: action.Id);
        work.Claim("history-budget-test", now);

        var previousPeriod = new ParkingBudgetPeriod(
            Guid.NewGuid(), correctedStart.AddHours(-1), visitStart, TimeSpan.FromMinutes(2));
        var currentPeriod = new ParkingBudgetPeriod(
            Guid.NewGuid(), visitStart, correctedStart.AddHours(1), TimeSpan.FromMinutes(30));
        var originalThresholds = Array.Empty<int>();

        await using (var seed = fixture.CreateDbContext())
        {
            var settings = await seed.ParkingSystemSettings.SingleAsync(ct);
            originalThresholds = settings.BudgetWarningThresholdPercentages.ToArray();
            settings.SetBudgetWarningThresholdPercentages([80]);
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitSchedulerWork.Add(work);
            seed.ParkingBudgetPeriods.AddRange(previousPeriod, currentPeriod);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            var historyReader = new PagedHistoryReader(new ProviderActionHistoryRecord(
                "provider-action-budget", "COMPLETED", correctedStart, actionEnd, 0.42m, "EUR"));
            await using (var scope = BuildServices(historyReader).CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
                var claimed = await dbContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
                await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                    .ProcessAsync(claimed, ct);
            }

            await using var verify = fixture.CreateDbContext();
            var warning = await verify.ParkingBudgetWarningStates
                .SingleAsync(x => x.ParkingBudgetPeriodId == previousPeriod.Id, ct);
            Assert.Equal(80, warning.ThresholdPercentage);
            Assert.Equal(correctedStart, (await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct)).ActualStartAt);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.VisitSchedulerWork.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.ProviderOperations.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingBudgetWarningStates
                .Where(x => x.ParkingBudgetPeriodId == previousPeriod.Id || x.ParkingBudgetPeriodId == currentPeriod.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Notifications.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.NotificationEvents.Where(x => x.AggregateId == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.VisitId == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Visits.Where(x => x.Id == visit.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingBudgetPeriods
                .Where(x => x.Id == previousPeriod.Id || x.Id == currentPeriod.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
            var settings = await cleanup.ParkingSystemSettings.SingleAsync(ct);
            settings.SetBudgetWarningThresholdPercentages(originalThresholds);
            await cleanup.SaveChangesAsync(ct);
        }
    }

    [Fact]
    public async Task Ambiguous_visit_recovery_preserves_provider_history_retry_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddMinutes(-30);
        var stoppedAt = now.AddMinutes(-10);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"ambiguous-history-{suffix}", $"AMBIGUOUS-HISTORY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"AH{suffix[..6]}", $"AH{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startedAt, now.AddHours(1), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, startedAt, now.AddMinutes(30), "product-1");
        action.MarkStarting();
        action.MarkActive("provider-action-ambiguous", startedAt);
        action.BeginStopping();
        action.MarkStopped(stoppedAt, "stopped");
        action.ScheduleHistoryReconciliation();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ReconcileProviderAction,
            now.AddHours(1), providerParkingActionId: action.Id);
        work.Claim("ambiguous-history-test", now);
        work.Release(now.AddHours(2));
        var originalDueAt = work.DueAt;

        var firstOperation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, action.Id, ProviderOperationType.Start);
        var secondOperation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visit.Id, action.Id, ProviderOperationType.Extend);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitSchedulerWork.Add(work);
            seed.ProviderOperations.AddRange(firstOperation, secondOperation);
            await seed.SaveChangesAsync(ct);
        }

        await using (var scope = BuildServices().CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>().RecoverAsync(ct);

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);
            var persistedWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            Assert.Equal(VisitHealth.AttentionRequired, persistedVisit.Health);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, persistedWork.Status);
            Assert.Equal(1, persistedWork.AttemptCount);
            Assert.InRange((originalDueAt - persistedWork.DueAt).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Recovery_recreates_missing_provider_history_work_idempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"history-recovery-{suffix}", $"HISTORY-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"HX{suffix[..6]}", $"HX{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            now.AddMinutes(-30), now.AddMinutes(-1), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(now);

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, now.AddMinutes(-30), now.AddMinutes(-1), "product-1");
        action.MarkStarting();
        action.MarkActive("provider-history-recovery", now.AddMinutes(-30));
        action.MarkCompleted(now.AddMinutes(-1));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            await seed.SaveChangesAsync(ct);
        }

        await using (var scope = BuildServices().CreateAsyncScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<IVisitRecoveryService>();
            await recovery.ReconcileActiveProviderActionsAsync(ct);
            await recovery.ReconcileActiveProviderActionsAsync(ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedAction = await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct);
            var historyWork = await verify.VisitSchedulerWork
                .Where(x => x.ProviderParkingActionId == action.Id)
                .ToListAsync(ct);

            Assert.Equal(ProviderHistoryStatus.Pending, persistedAction.HistoryStatus);
            Assert.Single(historyWork);
            Assert.Equal(VisitSchedulerWorkType.ReconcileProviderAction, historyWork[0].Type);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, historyWork[0].Status);
        }

        await ClearVisitStateAsync(ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_history_read_failure_retries_and_marks_action_incomplete_after_deadline(bool malformedJson)
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var startAt = now.AddMinutes(-30);
        var endAt = now.AddMinutes(-1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"history-timeout-{suffix}", $"HISTORY-TIMEOUT-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"HT{suffix[..6]}", $"HT{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), user.Id, vehicle.Id, user.Id,
            startAt, now.AddHours(1), new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(now);

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, startAt, now.AddMinutes(30), "product-1");
        action.MarkStarting();
        action.MarkActive("provider-action-timeout", startAt);
        action.BeginStopping();
        action.MarkStopped(endAt, "stopped");
        action.ScheduleHistoryReconciliation();

        var work = new VisitSchedulerWork(
            Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ReconcileProviderAction,
            now.AddMinutes(-1), providerParkingActionId: action.Id);
        work.Claim("history-timeout-test", now);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            seed.ProviderParkingActions.Add(action);
            seed.VisitSchedulerWork.Add(work);
            await seed.SaveChangesAsync(ct);
        }

        IProviderActionHistoryReader historyReader = malformedJson
            ? new MalformedHistoryReader()
            : new UnavailableHistoryReader();
        await using (var scope = BuildServices(historyReader).CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
            var claimed = await dbContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                .ProcessAsync(claimed, ct);
        }

        var retryNow = DateTimeOffset.UtcNow;
        await using (var verify = fixture.CreateDbContext())
        {
            var retryWork = await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct);
            Assert.Equal(VisitSchedulerWorkStatus.Pending, retryWork.Status);
            Assert.True(retryWork.DueAt > retryNow);
            Assert.Equal(ProviderHistoryStatus.Pending,
                (await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct)).HistoryStatus);

            await verify.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE visit_scheduler_work SET \"CreatedAt\" = {retryNow.AddHours(-7)}, \"DueAt\" = {retryNow.AddMinutes(-1)} WHERE \"Id\" = {work.Id}",
                ct);
        }

        await using (var scope = BuildServices(historyReader).CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var claimer = services.GetRequiredService<IVisitSchedulerWorkClaimer>();
            var claimed = await claimer.ClaimNextDueAsync("history-timeout-final", DateTimeOffset.UtcNow, ct);
            Assert.NotNull(claimed);
            await services.GetRequiredService<IVisitSchedulerWorkProcessor>().ProcessAsync(claimed, ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            Assert.Equal(ProviderHistoryStatus.Incomplete,
                (await verify.ProviderParkingActions.SingleAsync(x => x.Id == action.Id, ct)).HistoryStatus);
            Assert.Equal(VisitSchedulerWorkStatus.Completed,
                (await verify.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, ct)).Status);
        }

        await ClearVisitStateAsync(ct);
    }

    [Fact]
    public async Task Manual_stop_keeps_manual_reason_and_supplied_actual_end()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearVisitStateAsync(ct);

        var startAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var desiredEndAt = startAt.AddHours(2);
        var actualEndAt = startAt.AddMinutes(20);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"manual-stop-{suffix}", $"MANUAL-STOP-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"MB{suffix[..6]}", $"MB{suffix[..6]}", null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            desiredEndAt,
            new EffectiveParkingPolicySnapshot(null, TimeSpan.FromHours(8), true));
        visit.Activate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.Vehicles.Add(vehicle);
            seed.Visits.Add(visit);
            await seed.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices();
        StopVisitClaim claim;
        await using (var claimScope = provider.CreateAsyncScope())
        {
            claim = await claimScope.ServiceProvider.GetRequiredService<IStopVisitClaimer>()
                .ClaimAsync(new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id), ct);
        }

        await using (var finalizeScope = provider.CreateAsyncScope())
        {
            await finalizeScope.ServiceProvider.GetRequiredService<IStopVisitFinalizer>()
                .CompleteWithoutProviderActionAsync(claim, actualEndAt, ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var persistedVisit = await verify.Visits.SingleAsync(x => x.Id == visit.Id, ct);

            Assert.Equal(VisitStatus.Completed, persistedVisit.Status);
            Assert.Equal(VisitEndReason.ManualStop, persistedVisit.EndReason);
            Assert.NotNull(persistedVisit.ActualEndAt);
            Assert.InRange(
                (persistedVisit.ActualEndAt.Value - actualEndAt).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
        }

        await ClearVisitStateAsync(ct);
    }

    private ServiceProvider BuildServices(IProviderActionHistoryReader? historyReader = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IParkingProvider, NoOpParkingProvider>();
        if (historyReader is not null)
            services.AddSingleton(historyReader);
        return services.BuildServiceProvider();
    }

    private async Task ClearVisitStateAsync(CancellationToken ct)
    {
        await using var cleanup = fixture.CreateDbContext();
        await cleanup.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await cleanup.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await cleanup.ProviderOperations.ExecuteDeleteAsync(ct);
        await cleanup.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await cleanup.Notifications.ExecuteDeleteAsync(ct);
        await cleanup.NotificationEvents.ExecuteDeleteAsync(ct);
        await cleanup.Visits.ExecuteDeleteAsync(ct);
    }

    private sealed class NoOpParkingProvider : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderCategory>>(Array.Empty<ProviderCategory>());

        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>(Array.Empty<Parkeren.Application.ParkingProvider.ProviderParkingAction>());

        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PagedHistoryReader(ProviderActionHistoryRecord record)
        : IProviderActionHistoryReader
    {
        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string providerProductId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(pageNumber == 0
                ? new ProviderActionHistoryPage(
                    Enumerable.Range(0, pageSize)
                        .Select(index => new ProviderActionHistoryRecord(
                            $"other-action-{index}", "COMPLETED", record.ActualStartAt,
                            record.ActualEndAt, 0m, "EUR"))
                        .ToArray(),
                    pageNumber,
                    pageSize,
                    pageSize + 1)
                : new ProviderActionHistoryPage([record], pageNumber, pageSize, pageSize + 1));
    }

    private sealed class UnavailableHistoryReader : IProviderActionHistoryReader
    {
        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string providerProductId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderActionHistoryPage>(new HttpRequestException("History provider is unavailable."));
    }

    private sealed class MalformedHistoryReader : IProviderActionHistoryReader
    {
        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string providerProductId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderActionHistoryPage>(new JsonException("Provider returned malformed history JSON."));
    }
}
