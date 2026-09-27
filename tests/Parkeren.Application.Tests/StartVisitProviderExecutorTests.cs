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
    public async Task In_progress_replay_waits_without_mutating_live_attempt()
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
        Assert.Equal(0, resultStore.UnknownCalls);
        Assert.Null(resultStore.LastErrorCode);
        Assert.Equal(ProviderOperationStatus.InProgress, operation.Status);
        Assert.Equal(ProviderActionState.Starting, action.State);
    }

    [Fact]
    public async Task Newly_claimed_retry_attempt_may_start_provider_once()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end,
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        var providerAction = new Parkeren.Application.ParkingProvider.ProviderParkingAction(
            "provider-1", "TK01HF", start, end, "test", "active");
        var provider = new SuccessfulProvider(providerAction);
        var resultStore = new NoopResultStore();

        var result = await new StartVisitProviderExecutor(provider, resultStore).ExecuteAsync(
            new ProviderStartPreparation(operation, action, true, AttemptStartedNow: true),
            new("TK01HF", "test", end),
            TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.Equal(1, provider.StartCalls);
    }

    [Fact]
    public async Task Unknown_replay_reconciles_existing_provider_action_without_second_start()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end,
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.MarkUnknown();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();
        operation.MarkUnknown("timeout");

        var providerAction = new Parkeren.Application.ParkingProvider.ProviderParkingAction(
            "provider-1", "TK01HF", start, end, "test", "active");
        var provider = new SuccessfulProvider(providerAction);
        var resultStore = new TrackingResultStore();
        var reconciler = new StartVisitProviderReconciler(provider, resultStore);

        var result = await new StartVisitProviderExecutor(provider, resultStore, reconciler).ExecuteAsync(
            new ProviderStartPreparation(operation, action, true),
            new("TK01HF", "test", end),
            TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.NotNull(result.ProviderAction);
        Assert.Equal("provider-1", result.ProviderAction.ProviderActionId);
        Assert.Equal(0, provider.StartCalls);
    }


    [Fact]
    public async Task Concurrent_replay_does_not_interrupt_live_provider_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end,
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        var provider = new BlockingProvider();
        var resultStore = new TrackingResultStore();
        var executor = new StartVisitProviderExecutor(provider, resultStore);
        var request = new ProviderStartRequest("TK01HF", "test", end);

        var liveAttempt = executor.ExecuteAsync(
            new ProviderStartPreparation(operation, action, false, AttemptStartedNow: true),
            request,
            cancellationToken);

        await provider.StartEntered.Task.WaitAsync(cancellationToken);

        var replay = await executor.ExecuteAsync(
            new ProviderStartPreparation(operation, action, true, AttemptStartedNow: false),
            request,
            cancellationToken);

        Assert.True(replay.RequiresReconciliation);
        Assert.Equal(1, provider.StartCalls);
        Assert.Equal(0, resultStore.UnknownCalls);
        Assert.Equal(ProviderOperationStatus.InProgress, operation.Status);
        Assert.Equal(ProviderActionState.Starting, action.State);

        provider.ReleaseStart.SetResult();
        await liveAttempt;
    }

    private sealed class BlockingProvider : IParkingProvider
    {
        public int StartCalls { get; private set; }
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            StartEntered.TrySetResult();
            await ReleaseStart.Task.WaitAsync(cancellationToken);
            return new("provider-live", request.LicensePlate, request.Start, request.End, request.Location, "active");
        }

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>(
                [new("provider-live", "TK01HF", DateTimeOffset.MinValue, DateTimeOffset.MinValue, "test", "active")]);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SuccessfulProvider(Parkeren.Application.ParkingProvider.ProviderParkingAction action) : IParkingProvider
    {
        public int StartCalls { get; private set; }

        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return Task.FromResult(action);
        }

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>([action]);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
