using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Xunit;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Tests;

public sealed class ContinueVisitProviderMatchingPolicyTests
{
    [Fact]
    public async Task Reconciler_accepts_requested_end_within_central_tolerance()
    {
        var requestedEnd = DateTimeOffset.Parse("2026-10-02T14:00:00+00:00");
        var preparation = CreateUnknownPreparation(requestedEnd);
        var remote = RemoteAction(preparation, requestedEnd.AddSeconds(4));
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderReconciler(
                new SequencedProvider([remote]), store)
            .ReconcileAsync(preparation, TestContext.Current.CancellationToken);

        Assert.Same(remote, result);
        Assert.Equal(1, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Reconciling, preparation.Operation.Status);
    }

    [Fact]
    public async Task Precheck_accepts_provider_end_within_tolerance_without_extending_again()
    {
        var requestedEnd = DateTimeOffset.Parse("2026-10-02T14:00:00+00:00");
        var preparation = CreateInProgressPreparation(requestedEnd);
        var remote = RemoteAction(preparation, requestedEnd.AddSeconds(-5));
        var provider = new SequencedProvider([remote]);
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderExecutor(provider, store)
            .ExecuteAsync(preparation, TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.Same(remote, result.ProviderAction);
        Assert.Equal(0, provider.ExtendCalls);
        Assert.Equal(1, store.ConfirmedCalls);
    }

    [Fact]
    public async Task Extend_readback_accepts_provider_end_within_central_tolerance()
    {
        var requestedEnd = DateTimeOffset.Parse("2026-10-02T14:00:00+00:00");
        var preparation = CreateInProgressPreparation(requestedEnd);
        var before = RemoteAction(preparation, requestedEnd.AddMinutes(-30));
        var after = RemoteAction(preparation, requestedEnd.AddSeconds(4));
        var provider = new SequencedProvider([before], [after]);
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderExecutor(provider, store)
            .ExecuteAsync(preparation, TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.Same(after, result.ProviderAction);
        Assert.Equal(1, provider.ExtendCalls);
        Assert.Equal(1, store.ConfirmedCalls);
        Assert.Null(store.UnknownErrorCode);
    }

    private static ProviderExtendPreparation CreateUnknownPreparation(DateTimeOffset requestedEnd)
    {
        var preparation = CreateInProgressPreparation(requestedEnd);
        preparation.Operation.MarkUnknown("timeout");
        return preparation with { IsReplay = true, AttemptStartedNow = false };
    }

    private static ProviderExtendPreparation CreateInProgressPreparation(DateTimeOffset requestedEnd)
    {
        var start = requestedEnd.AddHours(-2);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), Guid.NewGuid(), start, requestedEnd.AddHours(-1));
        action.MarkStarting();
        action.MarkActive("provider-1", start, "active");

        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Extend);
        operation.SetRequestedEndAt(requestedEnd);
        operation.BeginAttempt();

        return new ProviderExtendPreparation(operation, action, requestedEnd, false, AttemptStartedNow: true);
    }

    private static ProviderAction RemoteAction(
        ProviderExtendPreparation preparation,
        DateTimeOffset end) =>
        new(
            preparation.Action.ProviderActionId!,
            "TK01HF",
            preparation.Action.PlannedStartAt,
            end,
            "OSS Zone J",
            "active");

    private sealed class TrackingResultStore : IProviderExtendResultStore
    {
        public int ConfirmedCalls { get; private set; }
        public string? UnknownErrorCode { get; private set; }

        public Task RecordConfirmedAsync(
            ProviderExtendPreparation preparation,
            ProviderAction providerAction,
            CancellationToken cancellationToken = default)
        {
            ConfirmedCalls++;
            return Task.CompletedTask;
        }

        public Task RecordUnknownAsync(
            ProviderExtendPreparation preparation,
            string errorCode,
            CancellationToken cancellationToken = default)
        {
            UnknownErrorCode = errorCode;
            return Task.CompletedTask;
        }

        public Task RecordDefinitiveFailureAsync(
            ProviderExtendPreparation preparation,
            string errorCode,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecordStopRaceReadBackAsync(
            ProviderExtendPreparation preparation,
            ProviderAction providerAction,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecordBlockedAsync(
            ProviderExtendPreparation preparation,
            string reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SequencedProvider : IParkingProvider
    {
        private readonly Queue<IReadOnlyList<ProviderAction>> reads;

        public SequencedProvider(params IReadOnlyList<ProviderAction>[] reads)
        {
            this.reads = new Queue<IReadOnlyList<ProviderAction>>(reads);
        }

        public int ExtendCalls { get; private set; }

        public Task<IReadOnlyList<ProviderAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(reads.Count > 1 ? reads.Dequeue() : reads.Peek());

        public Task<ProviderAction> ExtendActionAsync(
            string providerActionId,
            DateTimeOffset newEnd,
            CancellationToken cancellationToken = default)
        {
            ExtendCalls++;
            return Task.FromResult(new ProviderAction(
                providerActionId, "TK01HF", newEnd.AddHours(-2), newEnd, "OSS_J", "active"));
        }

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
