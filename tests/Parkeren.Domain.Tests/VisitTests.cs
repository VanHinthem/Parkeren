using Xunit;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Domain.Tests;

public sealed class VisitTests
{
    private static Visit CreateVisit(EffectiveParkingPolicySnapshot? policySnapshot = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        DateTimeOffset.UtcNow, null,
        policySnapshot ?? EffectiveParkingPolicySnapshot.Capture(
            new EffectiveParkingPolicy(TimeSpan.FromHours(8), null, true)));

    [Fact]
    public void New_visit_starts_healthy_and_occupies_capacity()
    {
        var visit = CreateVisit();
        Assert.Equal(VisitStatus.Starting, visit.Status);
        Assert.Equal(VisitHealth.Healthy, visit.Health);
        Assert.Null(visit.EndReason);
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
        Assert.Equal(VisitEndReason.ManualStop, visit.EndReason);
        Assert.False(visit.OccupiesCapacity);
    }

    [Fact]
    public void Begin_stopping_records_explicit_end_reason()
    {
        var visit = CreateVisit();
        visit.Activate();

        visit.BeginStopping(VisitEndReason.MaxPaidParkingDurationReached);

        Assert.Equal(VisitStatus.Stopping, visit.Status);
        Assert.Equal(VisitEndReason.MaxPaidParkingDurationReached, visit.EndReason);
    }

    [Fact]
    public void End_reason_cannot_be_replaced_after_stopping_has_started()
    {
        var visit = CreateVisit();
        visit.Activate();
        visit.BeginStopping(VisitEndReason.DesiredEndReached);

        Assert.Throws<InvalidOperationException>(() =>
            visit.BeginStopping(VisitEndReason.ManualStop));
        Assert.Equal(VisitEndReason.DesiredEndReached, visit.EndReason);
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
    public void Active_visit_can_change_desired_end_without_changing_policy_snapshot()
    {
        var visit = CreateVisit();
        visit.Activate();
        var policySnapshot = visit.PolicySnapshot;
        var desiredEndAt = visit.StartAt.AddHours(2);

        visit.ChangeDesiredEndAt(desiredEndAt);

        Assert.Equal(desiredEndAt, visit.DesiredEndAt);
        Assert.Same(policySnapshot, visit.PolicySnapshot);

        visit.ChangeDesiredEndAt(null);
        Assert.Null(visit.DesiredEndAt);
        Assert.Same(policySnapshot, visit.PolicySnapshot);
    }

    [Fact]
    public void Stopping_visit_rejects_desired_end_change()
    {
        var visit = CreateVisit();
        visit.Activate();
        visit.BeginStopping();

        Assert.Throws<InvalidOperationException>(() => visit.ChangeDesiredEndAt(visit.StartAt.AddHours(2)));
    }

    [Fact]
    public void Desired_end_must_remain_after_visit_start()
    {
        var visit = CreateVisit();
        visit.Activate();

        Assert.Throws<ArgumentOutOfRangeException>(() => visit.ChangeDesiredEndAt(visit.StartAt));
    }

    [Fact]
    public void Invalid_lifecycle_transition_is_rejected()
    {
        var visit = CreateVisit();
        Assert.Throws<InvalidOperationException>(() => visit.Complete(visit.StartAt.AddHours(1)));
    }

    [Fact]
    public void Active_visit_rejects_manual_stop_when_snapshot_disallows_it()
    {
        var visit = CreateVisit(new EffectiveParkingPolicySnapshot(
            TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false));
        visit.Activate();

        Assert.Throws<InvalidOperationException>(() => visit.ChangeDesiredEndAt(null));
    }

    [Fact]
    public void Active_visit_allows_manual_stop_when_snapshot_allows_it()
    {
        var visit = CreateVisit(new EffectiveParkingPolicySnapshot(
            TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, true));
        visit.Activate();

        visit.ChangeDesiredEndAt(null);

        Assert.Null(visit.DesiredEndAt);
    }
}
