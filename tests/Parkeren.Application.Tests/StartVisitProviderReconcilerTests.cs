using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class StartVisitProviderReconcilerTests
{
    [Fact]
    public async Task No_provider_match_without_persisted_id_marks_attempt_retryable()
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
        var store = new TrackingResultStore();

        var reconciledAction = await new StartVisitProviderReconciler(new EmptyProvider(), store).ReconcileAsync(
            new ProviderStartPreparation(operation, action, true),
            "TK01HF",
            TestContext.Current.CancellationToken);

        Assert.Null(reconciledAction);
        Assert.Equal(1, store.RetryableCalls);
    }

    [Fact]
    public async Task No_provider_match_with_persisted_id_stays_unknown()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(1);
        var visit = new Visit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end,
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.CaptureStartResponse("provider-1", start, "active");
        action.MarkUnknown();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();
        operation.MarkUnknown("readback-unconfirmed");
        var store = new TrackingResultStore();

        var reconciledAction = await new StartVisitProviderReconciler(new EmptyProvider(), store).ReconcileAsync(
            new ProviderStartPreparation(operation, action, true),
            "TK01HF",
            TestContext.Current.CancellationToken);

        Assert.Null(reconciledAction);
        Assert.Equal(0, store.RetryableCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, operation.Status);
        Assert.Equal(ProviderActionHealth.Unknown, action.Health);
        Assert.Equal("provider-1", action.ProviderActionId);
    }

    [Fact]
    public async Task Externally_stopped_start_is_not_confirmed_or_retried()
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
        var store = new TrackingResultStore();
        var stopped = new Parkeren.Application.ParkingProvider.ProviderParkingAction(
            "provider-1", "TK01HF", start, end, "Oss", "stopped");

        var result = await new StartVisitProviderReconciler(new ActionsProvider([stopped]), store).ReconcileAsync(
            new ProviderStartPreparation(operation, action, true), "TK01HF", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, store.RetryableCalls);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, operation.Status);
    }

    [Fact]
    public async Task Start_with_externally_changed_end_is_not_retried_without_provider_id()
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
        var store = new TrackingResultStore();
        var changed = new Parkeren.Application.ParkingProvider.ProviderParkingAction(
            "provider-1", "TK-01-HF", start, end.AddMinutes(-15), "Oss", "active");

        var result = await new StartVisitProviderReconciler(new ActionsProvider([changed]), store).ReconcileAsync(
            new ProviderStartPreparation(operation, action, true), "TK01HF", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, store.RetryableCalls);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, operation.Status);
    }

    private sealed class TrackingResultStore : IProviderStartResultStore
    {
        public int RetryableCalls { get; private set; }
        public int ConfirmedCalls { get; private set; }

        public Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default)
        {
            RetryableCalls++;
            return Task.CompletedTask;
        }

        public Task RecordResponseAsync(ProviderStartPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordConfirmedAsync(ProviderStartPreparation preparation, Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction, CancellationToken cancellationToken = default)
        {
            ConfirmedCalls++;
            return Task.CompletedTask;
        }
        public Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyProvider : IParkingProvider
    {
        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>([]);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ActionsProvider(IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction> actions) : IParkingProvider
    {
        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(actions);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
