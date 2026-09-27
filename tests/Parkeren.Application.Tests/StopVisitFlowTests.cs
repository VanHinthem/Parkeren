using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class StopVisitFlowTests
{
    [Fact]
    public async Task Provider_free_stop_completes_without_touching_provider()
    {
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddHours(-1), now.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        visit.Activate();
        visit.BeginStopping();

        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visit.Id, null, ProviderOperationType.Stop);
        var claim = new StopVisitClaim(visit, operation, false, false);
        var claimer = new FixedClaimer(claim);
        var finalizer = new ProviderFreeFinalizer();
        var stopStore = new ThrowingStopStore();
        var provider = new ThrowingProvider();
        var executor = new StopVisitProviderExecutor(provider, new ThrowingResultStore());

        var result = await new StopVisitFlow(claimer, finalizer, stopStore, executor).StopAsync(
            new StopVisitCommand(operation.OperationId, visit.Id, visit.UserId),
            new StopVisitContext(new StopVisitActor(visit.UserId, UserRole.Visitor, true), visit),
            TestContext.Current.CancellationToken);

        Assert.Equal(StopVisitFlowOutcome.Completed, result.Outcome);
        Assert.False(result.IsReplay);
        Assert.True(finalizer.Completed);
        Assert.Equal(VisitStatus.Completed, result.Visit.Status);
    }

    [Fact]
    public async Task Unconfirmed_provider_stop_requires_reconciliation()
    {
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddHours(-1), now.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        visit.Activate();
        visit.BeginStopping();

        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, now.AddHours(-1), now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-1", now.AddHours(-1), "active");
        action.BeginStopping();

        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visit.Id, action.Id, ProviderOperationType.Stop);
        operation.BeginAttempt();
        var preparation = new ProviderStopPreparation(operation, action, false, true);
        var resultStore = new RecordingResultStore();
        var flow = new StopVisitFlow(
            new FixedClaimer(new StopVisitClaim(visit, operation, false, false)),
            new ProviderRequiredFinalizer(),
            new FixedStopStore(preparation),
            new StopVisitProviderExecutor(new UnconfirmedStopProvider(), resultStore));

        var result = await flow.StopAsync(
            new StopVisitCommand(operation.OperationId, visit.Id, visit.UserId),
            new StopVisitContext(new StopVisitActor(visit.UserId, UserRole.Visitor, true), visit),
            TestContext.Current.CancellationToken);

        Assert.Equal(StopVisitFlowOutcome.ReconciliationRequired, result.Outcome);
        Assert.True(resultStore.UnknownRecorded);
        Assert.Equal(VisitStatus.Stopping, result.Visit.Status);
    }

    private sealed class FixedClaimer(StopVisitClaim claim) : IStopVisitClaimer
    {
        public Task<StopVisitClaim> ClaimAsync(StopVisitCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(claim);
    }

    private sealed class ProviderFreeFinalizer : IStopVisitFinalizer
    {
        public bool Completed { get; private set; }

        public Task<bool> RequiresProviderActionAsync(StopVisitClaim claim, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Visit> CompleteWithoutProviderActionAsync(StopVisitClaim claim, DateTimeOffset actualEndAt, CancellationToken cancellationToken = default)
        {
            claim.Visit.Complete(actualEndAt);
            Completed = true;
            return Task.FromResult(claim.Visit);
        }
    }

    private sealed class ProviderRequiredFinalizer : IStopVisitFinalizer
    {
        public Task<bool> RequiresProviderActionAsync(StopVisitClaim claim, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<Visit> CompleteWithoutProviderActionAsync(StopVisitClaim claim, DateTimeOffset actualEndAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class FixedStopStore(ProviderStopPreparation preparation) : IProviderStopStore
    {
        public Task<ProviderStopPreparation> PrepareAttemptAsync(StopVisitClaim claim, CancellationToken cancellationToken = default) => Task.FromResult(preparation);
    }

    private sealed class RecordingResultStore : IProviderStopResultStore
    {
        public bool UnknownRecorded { get; private set; }
        public Task RecordUnknownAsync(ProviderStopPreparation preparation, string errorCode, CancellationToken cancellationToken = default)
        {
            preparation.Action.MarkUnknown();
            preparation.Operation.MarkUnknown(errorCode);
            UnknownRecorded = true;
            return Task.CompletedTask;
        }
        public Task RecordConfirmedAsync(ProviderStopPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, DateTimeOffset actualEndAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class UnconfirmedStopProvider : IParkingProvider
    {
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>([]);
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class ThrowingStopStore : IProviderStopStore
    {
        public Task<ProviderStopPreparation> PrepareAttemptAsync(StopVisitClaim claim, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider Stop store must not be used for a provider-free Stop.");
    }

    private sealed class ThrowingResultStore : IProviderStopResultStore
    {
        public Task RecordUnknownAsync(ProviderStopPreparation preparation, string errorCode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
        public Task RecordConfirmedAsync(ProviderStopPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, DateTimeOffset actualEndAt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
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
