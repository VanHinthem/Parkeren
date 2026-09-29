using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class ChangeVisitEndTimeFlowTests
{
    [Fact]
    public async Task Elapsed_end_time_routes_exclusively_through_stop_flow()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), userId, Guid.NewGuid(), userId,
            now.AddHours(-1), now.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        visit.Activate();

        var operationId = Guid.NewGuid();
        var stopFlow = new StopVisitFlow(
            new CompletingClaimer(visit, operationId),
            new ProviderFreeFinalizer(),
            new ThrowingStopStore(),
            new StopVisitProviderExecutor(new ThrowingProvider(), new ThrowingResultStore()),
            new FixedTimeProvider(now));
        var flow = new ChangeVisitEndTimeFlow(new ThrowingChanger(), stopFlow, null, new FixedTimeProvider(now));

        var result = await flow.ChangeAsync(
            new ChangeVisitEndTimeCommand(operationId, visit.Id, userId, now),
            new StopVisitContext(new StopVisitActor(userId, UserRole.Visitor, true), visit),
            TestContext.Current.CancellationToken);

        Assert.Equal(ChangeVisitEndTimeFlowOutcome.Stopped, result.Outcome);
        Assert.Equal(VisitStatus.Completed, result.Visit.Status);
    }


    [Fact]
    public async Task Provider_reconciliation_prevents_local_end_time_change()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var visit = new Visit(
            Guid.NewGuid(), Guid.NewGuid(), userId, Guid.NewGuid(), userId,
            now.AddHours(-1), now.AddHours(2),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        visit.Activate();

        var changer = new RecordingChanger(visit);
        var flow = new ChangeVisitEndTimeFlow(
            changer,
            new StopVisitFlow(
                new ThrowingClaimer(),
                new ProviderFreeFinalizer(),
                new ThrowingStopStore(),
                new StopVisitProviderExecutor(new ThrowingProvider(), new ThrowingResultStore()),
                new FixedTimeProvider(now)),
            new ReconciliationAdjuster(),
            new FixedTimeProvider(now));

        var result = await flow.ChangeAsync(
            new ChangeVisitEndTimeCommand(Guid.NewGuid(), visit.Id, userId, now.AddHours(1)),
            new StopVisitContext(new StopVisitActor(userId, UserRole.Visitor, true), visit),
            TestContext.Current.CancellationToken);

        Assert.Equal(ChangeVisitEndTimeFlowOutcome.ReconciliationRequired, result.Outcome);
        Assert.False(changer.WasCalled);
        Assert.Equal(now.AddHours(2), visit.DesiredEndAt);
    }

    private sealed class ThrowingChanger : IVisitEndTimeChanger
    {
        public Task<ChangeVisitEndTimeResult> PrepareAsync(ChangeVisitEndTimeCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("End-time changer must not run when the requested end has elapsed.");

        public Task<ChangeVisitEndTimeResult> ApplyAsync(ChangeVisitEndTimeCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("End-time changer must not run when the requested end has elapsed.");
    }


    private sealed class RecordingChanger(Visit visit) : IVisitEndTimeChanger
    {
        public bool WasCalled { get; private set; }

        public Task<ChangeVisitEndTimeResult> PrepareAsync(ChangeVisitEndTimeCommand command, CancellationToken cancellationToken = default)
        {
            var change = new VisitEndTimeChange(
                Guid.NewGuid(),
                command.OperationId,
                command.VisitId,
                command.ActorUserId,
                visit.DesiredEndAt,
                command.DesiredEndAt,
                DateTimeOffset.UtcNow);
            return Task.FromResult(new ChangeVisitEndTimeResult(visit, change, false));
        }

        public Task<ChangeVisitEndTimeResult> ApplyAsync(ChangeVisitEndTimeCommand command, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            var change = new VisitEndTimeChange(
                Guid.NewGuid(),
                command.OperationId,
                command.VisitId,
                command.ActorUserId,
                visit.DesiredEndAt,
                command.DesiredEndAt,
                DateTimeOffset.UtcNow);
            change.MarkApplied();
            return Task.FromResult(new ChangeVisitEndTimeResult(visit, change, false));
        }
    }

    private sealed class ReconciliationAdjuster : IVisitEndTimeProviderAdjuster
    {
        public Task<VisitEndTimeProviderAdjustmentResult> AdjustAsync(ChangeVisitEndTimeCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VisitEndTimeProviderAdjustmentResult(true));
    }

    private sealed class ThrowingClaimer : IStopVisitClaimer
    {
        public Task<StopVisitClaim> ClaimAsync(StopVisitCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private sealed class CompletingClaimer(Visit visit, Guid operationId) : IStopVisitClaimer
    {
        public Task<StopVisitClaim> ClaimAsync(StopVisitCommand command, CancellationToken cancellationToken = default)
        {
            visit.BeginStopping();
            return Task.FromResult(new StopVisitClaim(
                visit,
                new ProviderOperation(operationId, Guid.NewGuid(), visit.Id, null, ProviderOperationType.Stop),
                false,
                false));
        }
    }

    private sealed class ProviderFreeFinalizer : IStopVisitFinalizer
    {
        public Task<bool> RequiresProviderActionAsync(StopVisitClaim claim, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Visit> CompleteWithoutProviderActionAsync(StopVisitClaim claim, DateTimeOffset actualEndAt, CancellationToken cancellationToken = default)
        {
            claim.Visit.Complete(actualEndAt);
            return Task.FromResult(claim.Visit);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingStopStore : IProviderStopStore
    {
        public Task<ProviderStopPreparation> PrepareAttemptAsync(StopVisitClaim claim, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class ThrowingResultStore : IProviderStopResultStore
    {
        public Task RecordUnknownAsync(ProviderStopPreparation preparation, string errorCode, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task RecordConfirmedAsync(ProviderStopPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, DateTimeOffset actualEndAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class ThrowingProvider : IParkingProvider
    {
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
