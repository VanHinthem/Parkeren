using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class StopVisitProviderExecutorTests
{
    [Fact]
    public async Task Fresh_stop_attempt_calls_provider_once_and_requires_stopped_readback()
    {
        var now = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visitId, now.AddMinutes(-30), now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-stop-1", now.AddMinutes(-30), "active");
        action.BeginStopping();

        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visitId, action.Id, ProviderOperationType.Stop);
        operation.BeginAttempt();
        var preparation = new ProviderStopPreparation(operation, action, false, true);
        var provider = new SuccessfulStopProvider(
            new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                "provider-stop-1", "ST01OP", now.AddMinutes(-30), now.AddHours(1), "Oss", "stopped"));
        var store = new TrackingStopResultStore();

        var result = await new StopVisitProviderExecutor(provider, store).ExecuteAsync(
            preparation, TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.Equal(1, provider.StopCalls);
        Assert.Equal("provider-stop-1", provider.StoppedProviderActionId);
        Assert.Equal(1, provider.ReadCalls);
        Assert.Equal(1, store.ConfirmedCalls);
        Assert.Equal(0, store.UnknownCalls);
        Assert.Equal("provider-stop-1", store.ConfirmedAction?.ProviderActionId);
    }

    [Fact]
    public async Task In_progress_replay_never_calls_provider_again()
    {
        var now = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visitId, now.AddMinutes(-30), now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-stop-2", now.AddMinutes(-30), "active");
        action.BeginStopping();

        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visitId, action.Id, ProviderOperationType.Stop);
        operation.BeginAttempt();
        var provider = new SuccessfulStopProvider(
            new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                "provider-stop-2", "ST02OP", now.AddMinutes(-30), now.AddHours(1), "Oss", "stopped"));
        var store = new TrackingStopResultStore();

        var result = await new StopVisitProviderExecutor(provider, store).ExecuteAsync(
            new ProviderStopPreparation(operation, action, true, false),
            TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Equal(0, provider.StopCalls);
        Assert.Equal(0, provider.ReadCalls);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(0, store.UnknownCalls);
    }

    [Fact]
    public async Task Unconfirmed_readback_marks_stop_unknown_instead_of_succeeding()
    {
        var now = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var action = new Parkeren.Domain.Visits.ProviderParkingAction(Guid.NewGuid(), visitId, now.AddMinutes(-30), now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-stop-3", now.AddMinutes(-30), "active");
        action.BeginStopping();

        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), visitId, action.Id, ProviderOperationType.Stop);
        operation.BeginAttempt();
        var provider = new SuccessfulStopProvider(
            new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                "provider-stop-3", "ST03OP", now.AddMinutes(-30), now.AddHours(1), "Oss", "active"));
        var store = new TrackingStopResultStore();

        var result = await new StopVisitProviderExecutor(provider, store).ExecuteAsync(
            new ProviderStopPreparation(operation, action, false, true),
            TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Equal(1, provider.StopCalls);
        Assert.Equal(1, provider.ReadCalls);
        Assert.Equal(0, store.ConfirmedCalls);
        Assert.Equal(1, store.UnknownCalls);
        Assert.Equal("read-back-unconfirmed", store.LastErrorCode);
    }

    private sealed class SuccessfulStopProvider(
        Parkeren.Application.ParkingProvider.ProviderParkingAction action) : IParkingProvider
    {
        public int StopCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public string? StoppedProviderActionId { get; private set; }

        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            StoppedProviderActionId = providerActionId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>> GetActionsAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<Parkeren.Application.ParkingProvider.ProviderParkingAction>>([action]);
        }

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Parkeren.Application.ParkingProvider.ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TrackingStopResultStore : IProviderStopResultStore
    {
        public int ConfirmedCalls { get; private set; }
        public int UnknownCalls { get; private set; }
        public Parkeren.Application.ParkingProvider.ProviderParkingAction? ConfirmedAction { get; private set; }
        public string? LastErrorCode { get; private set; }

        public Task RecordUnknownAsync(ProviderStopPreparation preparation, string errorCode, CancellationToken cancellationToken = default)
        {
            UnknownCalls++;
            LastErrorCode = errorCode;
            return Task.CompletedTask;
        }

        public Task RecordConfirmedAsync(
            ProviderStopPreparation preparation,
            Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction,
            DateTimeOffset actualEndAt,
            CancellationToken cancellationToken = default)
        {
            ConfirmedCalls++;
            ConfirmedAction = providerAction;
            return Task.CompletedTask;
        }
    }
}
