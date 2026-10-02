using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Xunit;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;
using StoredAction = Parkeren.Domain.Visits.ProviderParkingAction;

namespace Parkeren.Application.Tests;

public sealed class StartVisitProviderMatchingPolicyTests
{
    [Fact]
    public async Task Direct_readback_accepts_normalized_plate_and_timestamp_drift_within_tolerance()
    {
        var start = DateTimeOffset.Parse("2026-10-02T12:00:00+00:00");
        var end = start.AddHours(1);
        var preparation = CreateInProgressPreparation(start, end);
        var response = new ProviderAction("provider-1", "TK01HF", start, end, "OSS_J", "active");
        var readback = new ProviderAction("provider-1", "TK-01-HF", start.AddSeconds(4), end.AddSeconds(-4), "OSS Zone J", "active");
        var provider = new StartReadbackProvider(response, [readback]);

        var result = await new StartVisitProviderExecutor(provider, new NoopResultStore()).ExecuteAsync(
            preparation,
            new ProviderStartRequest("TK01HF", "OSS_J", end),
            TestContext.Current.CancellationToken);

        Assert.False(result.RequiresReconciliation);
        Assert.Same(readback, result.ProviderAction);
    }

    [Fact]
    public async Task Direct_readback_rejects_timestamp_drift_outside_tolerance()
    {
        var start = DateTimeOffset.Parse("2026-10-02T12:00:00+00:00");
        var end = start.AddHours(1);
        var preparation = CreateInProgressPreparation(start, end);
        var response = new ProviderAction("provider-1", "TK01HF", start, end, "OSS_J", "active");
        var readback = new ProviderAction("provider-1", "TK01HF", start.AddSeconds(6), end, "OSS_J", "active");
        var provider = new StartReadbackProvider(response, [readback]);

        var result = await new StartVisitProviderExecutor(provider, new NoopResultStore()).ExecuteAsync(
            preparation,
            new ProviderStartRequest("TK01HF", "OSS_J", end),
            TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Same(response, result.ProviderAction);
    }

    [Fact]
    public async Task Reconciler_accepts_unique_fallback_within_central_tolerance()
    {
        var start = DateTimeOffset.Parse("2026-10-02T12:00:00+00:00");
        var end = start.AddHours(1);
        var preparation = CreateUnknownPreparation(start, end);
        var readback = new ProviderAction("provider-1", "TK-01-HF", start.AddSeconds(-4), end.AddSeconds(4), "OSS Zone J", "scheduled");
        var provider = new StartReadbackProvider(readback, [readback]);

        var result = await new StartVisitProviderReconciler(provider, new NoopResultStore()).ReconcileAsync(
            preparation,
            "TK01HF",
            TestContext.Current.CancellationToken);

        Assert.Same(readback, result);
    }

    private static ProviderStartPreparation CreateInProgressPreparation(DateTimeOffset start, DateTimeOffset end)
    {
        var visit = CreateVisit(start, end);
        var action = new StoredAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();
        return new ProviderStartPreparation(operation, action, false, AttemptStartedNow: true);
    }

    private static ProviderStartPreparation CreateUnknownPreparation(DateTimeOffset start, DateTimeOffset end)
    {
        var visit = CreateVisit(start, end);
        var action = new StoredAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.MarkUnknown();
        var operation = new ProviderOperation(Guid.NewGuid(), visit.StartOperationId, visit.Id, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();
        operation.MarkUnknown("timeout");
        return new ProviderStartPreparation(operation, action, true);
    }

    private static Visit CreateVisit(DateTimeOffset start, DateTimeOffset end) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            end,
            EffectiveParkingPolicySnapshot.Capture(
                new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));

    private sealed class StartReadbackProvider(
        ProviderAction startResponse,
        IReadOnlyList<ProviderAction> actions) : IParkingProvider
    {
        public Task<IReadOnlyList<ProviderAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(actions);

        public Task<ProviderAction> StartActionAsync(
            ProviderParkingActionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(startResponse);

        public Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProviderAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopResultStore : IProviderStartResultStore
    {
        public Task RecordResponseAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordConfirmedAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
