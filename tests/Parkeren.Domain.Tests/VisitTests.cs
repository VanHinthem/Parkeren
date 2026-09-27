using Xunit;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Domain.Tests;

public sealed class VisitTests
{
    private static Visit CreateVisit() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        DateTimeOffset.UtcNow, null,
        EffectiveParkingPolicySnapshot.Capture(new EffectiveParkingPolicy(TimeSpan.FromHours(8), null, true)));

    [Fact]
    public void New_visit_starts_healthy_and_occupies_capacity()
    {
        var visit = CreateVisit();
        Assert.Equal(VisitStatus.Starting, visit.Status);
        Assert.Equal(VisitHealth.Healthy, visit.Health);
        Assert.True(visit.OccupiesCapacity);
    }

    [Fact]
    public void Completed_visit_releases_capacity()
    {
        var visit = CreateVisit();
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(visit.StartAt.AddHours(1));
        Assert.Equal(VisitStatus.Completed, visit.Status);
        Assert.False(visit.OccupiesCapacity);
    }

    [Fact]
    public void Health_is_independent_from_active_lifecycle()
    {
        var visit = CreateVisit();
        visit.Activate();
        visit.SetHealth(VisitHealth.Reconciling);
        Assert.Equal(VisitStatus.Active, visit.Status);
        Assert.Equal(VisitHealth.Reconciling, visit.Health);
        Assert.True(visit.OccupiesCapacity);
    }

    [Fact]
    public void Invalid_lifecycle_transition_is_rejected()
    {
        var visit = CreateVisit();
        Assert.Throws<InvalidOperationException>(() => visit.Complete(visit.StartAt.AddHours(1)));
    }
}
