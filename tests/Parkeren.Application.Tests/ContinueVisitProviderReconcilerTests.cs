using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Xunit;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Tests;

public sealed class ContinueVisitProviderReconcilerTests
{
    [Fact]
    public async Task Matching_provider_action_and_requested_end_is_confirmed()
    {
        var requestedEnd = DateTimeOffset.UtcNow.AddHours(1);
        var preparation = CreateUnknownPreparation(requestedEnd);
        var providerAction = new ProviderAction(
            preparation.Action.ProviderActionId!,
            "TK01HF",
            preparation.Action.PlannedStartAt,
            requestedEnd,
            "active");
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderReconciler(
            new ActionsProvider([providerAction]), store)
            .ReconcileAsync(preparation, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(1, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Reconciling, preparation.Operation.Status);
    }

    [Fact]
    public async Task No_provider_match_stays_unknown()
    {
        var preparation = CreateUnknownPreparation(DateTimeOffset.UtcNow.AddHours(1));
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderReconciler(
            new ActionsProvider([]), store)
            .ReconcileAsync(preparation, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, preparation.Operation.Status);
    }

    [Fact]
    public async Task Different_provider_end_is_not_confirmed()
    {
        var requestedEnd = DateTimeOffset.UtcNow.AddHours(1);
        var preparation = CreateUnknownPreparation(requestedEnd);
        var providerAction = new ProviderAction(
            preparation.Action.ProviderActionId!,
            "TK01HF",
            preparation.Action.PlannedStartAt,
            requestedEnd.AddMinutes(-5),
            "active");
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderReconciler(
            new ActionsProvider([providerAction]), store)
            .ReconcileAsync(preparation, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, preparation.Operation.Status);
    }

    private static ProviderExtendPreparation CreateUnknownPreparation(DateTimeOffset requestedEnd)
    {
        var start = requestedEnd.AddHours(-2);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, requestedEnd.AddHours(-1));
        action.MarkStarting();
        action.CaptureStartResponse("provider-1", start, "active");
        action.MarkActive();

        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Extend);
        operation.SetRequestedEndAt(requestedEnd);
        operation.BeginAttempt();
        operation.MarkUnknown("timeout");

        return new ProviderExtendPreparation(operation, action, requestedEnd, true);
    }

    private sealed class TrackingResultStore : IProviderExtendResultStore
    {
        public int ConfirmedCalls { get; private set; }

        public Task RecordConfirmedAsync(ProviderExtendPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
        {
            ConfirmedCalls++;
            return Task.CompletedTask;
        }

        public Task RecordUnknownAsync(ProviderExtendPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordDefinitiveFailureAsync(ProviderExtendPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ActionsProvider(IReadOnlyList<ProviderAction> actions) : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(actions);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
