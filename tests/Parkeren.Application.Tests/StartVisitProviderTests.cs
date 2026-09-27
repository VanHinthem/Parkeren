using Xunit;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Tests;

public sealed class StartVisitProviderTests
{
    [Fact]
    public void Paid_start_uses_same_operation_id_for_provider_mutation()
    {
        var operationId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        var visit = new Visit(Guid.NewGuid(), operationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(2),
            EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(4), null, true)));
        var claim = new StartVisitClaimResult(visit, false, true);

        var result = new StartVisitProviderPreparer().Prepare(claim, start.AddHours(2));

        Assert.Equal(operationId, result.Operation.OperationId);
        Assert.Equal(visit.Id, result.Operation.VisitId);
        Assert.Equal(result.Action.Id, result.Operation.ProviderParkingActionId);
        Assert.Equal(visit.Id, result.Action.VisitId);
        Assert.Equal(ProviderOperationType.Start, result.Operation.Type);
        Assert.Equal(ProviderOperationStatus.Pending, result.Operation.Status);
        Assert.Equal(ProviderActionState.Planned, result.Action.State);
    }
}
