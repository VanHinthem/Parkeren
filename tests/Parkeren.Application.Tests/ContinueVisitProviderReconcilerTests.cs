using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Xunit;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Tests;

public sealed class ContinueVisitProviderReconcilerTests
{

    [Fact]
    public async Task Succeeded_provider_extension_replay_does_not_call_provider_again()
    {
        var requestedEnd = DateTimeOffset.UtcNow.AddHours(1);
        var preparation = CreateSucceededPreparation(requestedEnd);
        var provider = new CountingProvider();
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderExecutor(provider, store)
            .ExecuteAsync(preparation, TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.False(result.DefinitiveFailure);
        Assert.Null(result.ProviderAction);
        Assert.Equal(0, provider.GetActionsCalls);
        Assert.Equal(0, provider.ExtendCalls);
        Assert.Equal(0, store.ConfirmedCalls);
    }

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
            "Oss",
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
            "Oss",
            "active");
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderReconciler(
            new ActionsProvider([providerAction]), store)
            .ReconcileAsync(preparation, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, preparation.Operation.Status);
    }

    [Fact]
    public async Task Stopped_provider_action_is_never_extended()
    {
        var requestedEnd = DateTimeOffset.UtcNow.AddHours(1);
        var preparation = CreateInProgressPreparation(requestedEnd);
        var providerAction = new ProviderAction(
            preparation.Action.ProviderActionId!, "TK01HF", preparation.Action.PlannedStartAt,
            preparation.Action.PlannedEndAt, "Oss", "stopped");
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderExecutor(new ActionsProvider([providerAction]), store)
            .ExecuteAsync(preparation, TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Equal("provider-action-not-active", store.UnknownErrorCode);
        Assert.Equal(0, store.ConfirmedCalls);
    }

    [Fact]
    public async Task Provider_response_error_during_extension_is_recorded_as_unknown()
    {
        var requestedEnd = DateTimeOffset.UtcNow.AddHours(1);
        var preparation = CreateInProgressPreparation(requestedEnd);
        var providerAction = new ProviderAction(
            preparation.Action.ProviderActionId!, "TK01HF", preparation.Action.PlannedStartAt,
            preparation.Action.PlannedEndAt, "Oss", "active");
        var store = new TrackingResultStore();
        const string providerDetails = "sensitive-provider-details";
        var provider = new ActionsProvider(
            [providerAction],
            new ProviderResponseException(
                "PROVIDER_FAILURE",
                providerDetails,
                $"provider error: {providerDetails}"));

        var result = await new ContinueVisitProviderExecutor(provider, store)
            .ExecuteAsync(preparation, TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.False(result.DefinitiveFailure);
        Assert.Equal("PROVIDER_FAILURE", store.UnknownErrorCode);
        Assert.DoesNotContain(providerDetails, store.UnknownErrorCode, StringComparison.Ordinal);
        Assert.Equal(0, store.ConfirmedCalls);
    }

    [Fact]
    public async Task Stopped_provider_action_cannot_confirm_unknown_extension()
    {
        var requestedEnd = DateTimeOffset.UtcNow.AddHours(1);
        var preparation = CreateUnknownPreparation(requestedEnd);
        var providerAction = new ProviderAction(
            preparation.Action.ProviderActionId!, "TK01HF", preparation.Action.PlannedStartAt,
            requestedEnd, "Oss", "stopped");
        var store = new TrackingResultStore();

        var result = await new ContinueVisitProviderReconciler(new ActionsProvider([providerAction]), store)
            .ReconcileAsync(preparation, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(ProviderOperationStatus.Unknown, preparation.Operation.Status);
    }


    private static ProviderExtendPreparation CreateSucceededPreparation(DateTimeOffset requestedEnd)
    {
        var start = requestedEnd.AddHours(-2);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, requestedEnd.AddHours(-1));
        action.MarkStarting();
        action.MarkActive("provider-1", start, "active");

        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Extend);
        operation.SetRequestedEndAt(requestedEnd);
        operation.BeginAttempt();
        operation.Succeed(DateTimeOffset.UtcNow);

        return new ProviderExtendPreparation(operation, action, requestedEnd, true);
    }

    private static ProviderExtendPreparation CreateUnknownPreparation(DateTimeOffset requestedEnd)
    {
        var start = requestedEnd.AddHours(-2);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, requestedEnd.AddHours(-1));
        action.MarkStarting();
        action.MarkActive("provider-1", start, "active");

        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Extend);
        operation.SetRequestedEndAt(requestedEnd);
        operation.BeginAttempt();
        operation.MarkUnknown("timeout");

        return new ProviderExtendPreparation(operation, action, requestedEnd, true);
    }

    private static ProviderExtendPreparation CreateInProgressPreparation(DateTimeOffset requestedEnd)
    {
        var start = requestedEnd.AddHours(-2);
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, requestedEnd.AddHours(-1));
        action.MarkStarting();
        action.MarkActive("provider-1", start, "active");
        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Extend);
        operation.SetRequestedEndAt(requestedEnd);
        operation.BeginAttempt();
        return new ProviderExtendPreparation(operation, action, requestedEnd, false);
    }

    private sealed class TrackingResultStore : IProviderExtendResultStore
    {
        public int ConfirmedCalls { get; private set; }
        public string? UnknownErrorCode { get; private set; }

        public Task RecordConfirmedAsync(ProviderExtendPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
        {
            ConfirmedCalls++;
            return Task.CompletedTask;
        }

        public Task RecordUnknownAsync(ProviderExtendPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
        {
            UnknownErrorCode = errorCode;
            return Task.CompletedTask;
        }
        public Task RecordDefinitiveFailureAsync(ProviderExtendPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecordStopRaceReadBackAsync(ProviderExtendPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecordBlockedAsync(ProviderExtendPreparation preparation, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }


    private sealed class CountingProvider : IParkingProvider
    {
        public int GetActionsCalls { get; private set; }
        public int ExtendCalls { get; private set; }

        public Task<IReadOnlyList<ProviderAction>> GetActionsAsync(CancellationToken cancellationToken = default)
        {
            GetActionsCalls++;
            return Task.FromResult<IReadOnlyList<ProviderAction>>([]);
        }

        public Task<ProviderAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default)
        {
            ExtendCalls++;
            throw new InvalidOperationException("A succeeded replay must not call the provider.");
        }

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ActionsProvider(
        IReadOnlyList<ProviderAction> actions,
        Exception? extensionException = null) : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(actions);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderAction>(extensionException ?? new NotSupportedException());
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
