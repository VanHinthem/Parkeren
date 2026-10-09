using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderParkingActionHistoryTests
{
    [Fact]
    public void New_action_starts_without_history_or_cost()
    {
        var action = new ProviderParkingAction(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(ProviderHistoryStatus.NotRequired, action.HistoryStatus);
        Assert.Null(action.ProviderCostAmount);
    }

    [Fact]
    public void Provider_history_corrects_interval_and_cost()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var action = CreateCompletedAction(startedAt);
        var historyStartedAt = startedAt.AddMinutes(1);
        var historyEndedAt = startedAt.AddMinutes(59);
        action.SetInitialProviderCost(0.30m);
        action.ScheduleHistoryReconciliation();

        action.ApplyProviderHistory(historyStartedAt, historyEndedAt, 0.29m);

        Assert.Equal(ProviderHistoryStatus.Reconciled, action.HistoryStatus);
        Assert.Equal(historyStartedAt, action.ActualStartAt);
        Assert.Equal(historyEndedAt, action.ActualEndAt);
        Assert.Equal(0.29m, action.ProviderCostAmount);
    }

    [Fact]
    public void History_without_cost_retains_initial_cost_and_marks_action_incomplete()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var action = CreateCompletedAction(startedAt);
        var historyStartedAt = startedAt.AddMinutes(1);
        var historyEndedAt = startedAt.AddMinutes(59);
        action.SetInitialProviderCost(0.30m);
        action.ScheduleHistoryReconciliation();

        action.ApplyProviderHistory(historyStartedAt, historyEndedAt, null);

        Assert.Equal(ProviderHistoryStatus.Incomplete, action.HistoryStatus);
        Assert.Equal(historyStartedAt, action.ActualStartAt);
        Assert.Equal(historyEndedAt, action.ActualEndAt);
        Assert.Equal(0.30m, action.ProviderCostAmount);
    }

    [Fact]
    public void Finalized_managed_history_can_be_corrected_after_initial_reconciliation()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var action = CreateCompletedAction(startedAt);
        action.SetInitialProviderCost(0.30m);
        action.ScheduleHistoryReconciliation();
        action.ApplyProviderHistory(startedAt, startedAt.AddHours(1), 0.30m);
        var observedAt = DateTimeOffset.UtcNow;
        var correctedStart = startedAt.AddMinutes(1);
        var correctedEnd = startedAt.AddMinutes(58);

        action.RefreshFinalizedManagedHistory(
            correctedStart, correctedEnd, 0.28m, "COMPLETED", observedAt);

        Assert.Equal(correctedStart, action.ActualStartAt);
        Assert.Equal(correctedEnd, action.ActualEndAt);
        Assert.Equal(0.28m, action.ProviderCostAmount);
        Assert.Equal("COMPLETED", action.ProviderStatus);
        Assert.Equal(ProviderHistoryStatus.Reconciled, action.HistoryStatus);
        Assert.Equal(observedAt, action.FirstObservedAt);
        Assert.Equal(observedAt, action.LastSyncedAt);
    }

    [Fact]
    public void Finalized_managed_history_cannot_refresh_before_initial_reconciliation_is_terminal()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var action = CreateCompletedAction(startedAt);

        Assert.Throws<InvalidOperationException>(() => action.RefreshFinalizedManagedHistory(
            startedAt, startedAt.AddHours(1), 0.30m, "COMPLETED", DateTimeOffset.UtcNow));
    }

    private static ProviderParkingAction CreateCompletedAction(DateTimeOffset startedAt)
    {
        var action = new ProviderParkingAction(
            Guid.NewGuid(), Guid.NewGuid(), startedAt, startedAt.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-123", startedAt);
        action.MarkCompleted(startedAt.AddHours(1));
        return action;
    }
}