using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;
public sealed class StartVisitTests
{
    [Fact]
    public void Prepare_captures_policy_and_keeps_visit_starting_until_capacity_and_provider_work_are_committed()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(12), true);
        var command = new StartVisitCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        var result = new StartVisitPreparer().Prepare(command, policy, true);
        Assert.Equal(VisitStatus.Starting, result.Visit.Status);
        Assert.True(result.RequiresProviderCoverageNow);
        Assert.Equal(command.OperationId, result.OperationId);
        Assert.Equal(policy.MaxPaidParkingDuration, result.Visit.PolicySnapshot.MaxPaidParkingDuration);
    }
}
