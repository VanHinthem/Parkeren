using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
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
            TestContext.Current.CancellationToken);

        Assert.Equal(StopVisitFlowOutcome.Completed, result.Outcome);
        Assert.False(result.IsReplay);
        Assert.True(finalizer.Completed);
        Assert.Equal(VisitStatus.Completed, result.Visit.Status);
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
