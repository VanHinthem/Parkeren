using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class VisitSchedulerWorkExecutionPolicyTests
{
    [Theory]
    [InlineData(VisitHealth.Healthy, VisitSchedulerWorkExecutionDecision.Execute)]
    [InlineData(VisitHealth.Reconciling, VisitSchedulerWorkExecutionDecision.Defer)]
    [InlineData(VisitHealth.AttentionRequired, VisitSchedulerWorkExecutionDecision.Defer)]
    [InlineData(VisitHealth.StopFailed, VisitSchedulerWorkExecutionDecision.Defer)]
    public void Continuation_on_active_visit_depends_on_health(
        VisitHealth health,
        VisitSchedulerWorkExecutionDecision expected)
    {
        var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(
            VisitSchedulerWorkType.ContinueProviderCoverage,
            VisitStatus.Active,
            health);

        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(VisitStatus.Starting, VisitSchedulerWorkExecutionDecision.Defer)]
    [InlineData(VisitStatus.Stopping, VisitSchedulerWorkExecutionDecision.Cancel)]
    [InlineData(VisitStatus.Completed, VisitSchedulerWorkExecutionDecision.Cancel)]
    [InlineData(VisitStatus.Cancelled, VisitSchedulerWorkExecutionDecision.Cancel)]
    public void Continuation_non_active_state_has_explicit_decision(
        VisitStatus status,
        VisitSchedulerWorkExecutionDecision expected)
    {
        foreach (var health in Enum.GetValues<VisitHealth>())
        {
            var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(
                VisitSchedulerWorkType.ContinueProviderCoverage,
                status,
                health);

            Assert.Equal(expected, decision);
        }
    }

    [Theory]
    [InlineData(VisitStatus.Starting, VisitSchedulerWorkExecutionDecision.Execute)]
    [InlineData(VisitStatus.Active, VisitSchedulerWorkExecutionDecision.Execute)]
    [InlineData(VisitStatus.Stopping, VisitSchedulerWorkExecutionDecision.Execute)]
    [InlineData(VisitStatus.Completed, VisitSchedulerWorkExecutionDecision.Cancel)]
    [InlineData(VisitStatus.Cancelled, VisitSchedulerWorkExecutionDecision.Cancel)]
    public void Stop_visit_is_not_blocked_by_health(
        VisitStatus status,
        VisitSchedulerWorkExecutionDecision expected)
    {
        foreach (var health in Enum.GetValues<VisitHealth>())
        {
            var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(
                VisitSchedulerWorkType.StopVisit,
                status,
                health);

            Assert.Equal(expected, decision);
        }
    }

    [Theory]
    [InlineData(VisitStatus.Starting, VisitSchedulerWorkExecutionDecision.Defer)]
    [InlineData(VisitStatus.Active, VisitSchedulerWorkExecutionDecision.Execute)]
    [InlineData(VisitStatus.Stopping, VisitSchedulerWorkExecutionDecision.Cancel)]
    [InlineData(VisitStatus.Completed, VisitSchedulerWorkExecutionDecision.Cancel)]
    [InlineData(VisitStatus.Cancelled, VisitSchedulerWorkExecutionDecision.Cancel)]
    public void Long_visit_warning_depends_on_lifecycle_not_health(
        VisitStatus status,
        VisitSchedulerWorkExecutionDecision expected)
    {
        foreach (var health in Enum.GetValues<VisitHealth>())
        {
            var decision = VisitSchedulerWorkExecutionPolicy.Evaluate(
                VisitSchedulerWorkType.LongVisitWarning,
                status,
                health);

            Assert.Equal(expected, decision);
        }
    }
}
