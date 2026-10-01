using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class StopVisitMissingProviderActionTests
{
    [Fact]
    public async Task Known_missing_provider_action_is_rechecked_without_sending_stop()
    {
        var now = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visitId, now.AddMinutes(-30), now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("missing-provider-action", now.AddMinutes(-30), "active");
        action.BeginStopping();

        var operation = new ProviderOperation(
            Guid.NewGuid(), Guid.NewGuid(), visitId, action.Id, ProviderOperationType.Stop);
        operation.BeginAttempt();

        var provider = new MissingActionProvider();
        var store = new RecordingResultStore();
        var executor = new StopVisitProviderExecutor(provider, store);

        var result = await executor.ExecuteAsync(
            new ProviderStopPreparation(operation, action, false, true, true),
            TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.Equal(1, provider.ReadCalls);
        Assert.Equal(0, provider.StopCalls);
        Assert.NotNull(store.ConfirmedAction);
        Assert.Equal("missing", store.ConfirmedAction!.Status);
        Assert.Equal("missing-provider-action", store.ConfirmedAction.ProviderActionId);
        Assert.Equal(0, store.UnknownCalls);
    }

    private sealed class MissingActionProvider : IParkingProvider
    {
        public int ReadCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>([]);
        }

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingResultStore : IProviderStopResultStore
    {
        public Parkeren.Application.ParkingProvider.ProviderParkingAction? ConfirmedAction { get; private set; }
        public int UnknownCalls { get; private set; }

        public Task RecordUnknownAsync(
            ProviderStopPreparation preparation,
            string errorCode,
            CancellationToken cancellationToken = default)
        {
            UnknownCalls++;
            return Task.CompletedTask;
        }

        public Task RecordConfirmedAsync(
            ProviderStopPreparation preparation,
            Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction,
            DateTimeOffset actualEndAt,
            CancellationToken cancellationToken = default)
        {
            ConfirmedAction = providerAction;
            return Task.CompletedTask;
        }
    }
}
