using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class StartVisitProviderExecutorTests
{
    [Fact]
    public async Task Unknown_operation_is_never_blindly_retried()
    {
        var start = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, start.AddHours(1));
        action.MarkStarting(); action.MarkUnknown();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt(); operation.MarkUnknown("timeout");
        var provider = new CountingProvider();

        var result = await new StartVisitProviderExecutor(provider, new NoopResultStore()).ExecuteAsync(
            new ProviderStartPreparation(operation, action, true), new("TK01HF", "test", start.AddHours(1)), TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Equal(0, provider.StartCalls);
    }

    [Fact]
    public async Task Interrupted_in_progress_replay_is_marked_unknown_without_second_provider_start()
    {
        var start = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, start.AddHours(1));
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();
        var provider = new CountingProvider();
        var resultStore = new TrackingResultStore();

        var result = await new StartVisitProviderExecutor(provider, resultStore).ExecuteAsync(
            new ProviderStartPreparation(operation, action, true, AttemptStartedNow: false),
            new("TK01HF", "test", start.AddHours(1)),
            TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Equal(0, provider.StartCalls);
        Assert.Equal(1, resultStore.UnknownCalls);
        Assert.Equal("interrupted-in-progress", resultStore.LastErrorCode);
    }

    private sealed class TrackingResultStore : IProviderStartResultStore
    {
        public int UnknownCalls { get; private set; }
        public string? LastErrorCode { get; private set; }

        public Task RecordResponseAsync(ProviderStartPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordConfirmedAsync(ProviderStartPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
        {
            UnknownCalls++;
            LastErrorCode = errorCode;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopResultStore : IProviderStartResultStore
    {
        public Task RecordResponseAsync(ProviderStartPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordConfirmedAsync(ProviderStartPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CountingProvider : IParkingProvider
    {
        public int StartCalls { get; private set; }
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) { StartCalls++; throw new NotSupportedException(); }
        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
