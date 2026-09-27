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

        var result = await new StartVisitProviderExecutor(provider).ExecuteAsync(
            new ProviderStartPreparation(operation, action, true), new("TK01HF", "test", start.AddHours(1)), TestContext.Current.CancellationToken);

        Assert.True(result.RequiresReconciliation);
        Assert.Equal(0, provider.StartCalls);
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
