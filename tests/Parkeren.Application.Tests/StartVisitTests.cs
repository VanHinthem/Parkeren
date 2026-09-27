using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Users;
using Xunit;

namespace Parkeren.Application.Tests;
public sealed class StartVisitTests
{
    [Fact]
    public void Prepare_captures_policy_and_keeps_visit_starting_until_capacity_and_provider_work_are_committed()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(12), true);
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        var context = new StartVisitContext(new(command.ActorUserId, UserRole.Visitor, true), new(command.OwnerUserId, true), new(command.VehicleId, true, true));
        var result = new StartVisitPreparer().Prepare(command, context, policy, true);
        Assert.Equal(VisitStatus.Starting, result.Visit.Status);
        Assert.True(result.RequiresProviderCoverageNow);
        Assert.Equal(command.OperationId, result.OperationId);
        Assert.Equal(policy.MaxPaidParkingDuration, result.Visit.PolicySnapshot.MaxPaidParkingDuration);
    }
}


public sealed class StartVisitClaimerTests
{
    [Fact]
    public async Task Claim_reuses_existing_visit_for_replayed_operation()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        var context = new StartVisitContext(new(command.ActorUserId, UserRole.Visitor, true), new(command.OwnerUserId, true), new(command.VehicleId, true, true));
        var preparation = new StartVisitPreparer().Prepare(command, context, policy, true);
        var existing = preparation.Visit;
        var claimer = new StartVisitClaimer(new ReplayCapacityClaimer(existing));

        var result = await claimer.ClaimAsync(preparation, 5, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsReplay);
        Assert.Same(existing, result.Visit);
        Assert.True(result.RequiresProviderCoverageNow);
    }

    private sealed class ReplayCapacityClaimer(Visit existing) : IVisitCapacityClaimer
    {
        public Task<VisitCapacityClaim> TryClaimAsync(Visit visit, int maxConcurrentVisits, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VisitCapacityClaim(true, existing, true));
    }
}
