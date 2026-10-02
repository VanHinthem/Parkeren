using Xunit;
using Parkeren.Domain.Visits;

namespace Parkeren.Domain.Tests;

public sealed class ProviderOperationStartupRecoveryTests
{
    [Fact]
    public void Stale_start_attempt_becomes_unknown()
    {
        var now = DateTimeOffset.UtcNow;
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(-10), now.AddHours(1));
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        var changed = ProviderOperationStartupRecovery.MarkStaleInProgressUnknown(
            operation,
            action,
            now.Add(ProviderOperationStartupRecovery.AttemptLease).AddSeconds(1));

        Assert.True(changed);
        Assert.Equal(ProviderOperationStatus.Unknown, operation.Status);
        Assert.Equal("stale-in-progress", operation.LastErrorCode);
        Assert.Equal(ProviderActionHealth.Unknown, action.Health);
        Assert.Equal(ProviderActionState.Starting, action.State);
    }

    [Fact]
    public void Recent_attempt_remains_in_progress()
    {
        var now = DateTimeOffset.UtcNow;
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(1));
        action.MarkStarting();
        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Start);
        operation.BeginAttempt();

        var changed = ProviderOperationStartupRecovery.MarkStaleInProgressUnknown(
            operation,
            action,
            operation.AttemptStartedAt!.Value.Add(ProviderOperationStartupRecovery.AttemptLease).AddTicks(-1));

        Assert.False(changed);
        Assert.Equal(ProviderOperationStatus.InProgress, operation.Status);
        Assert.Equal(ProviderActionHealth.Healthy, action.Health);
    }

    [Fact]
    public void Stale_extend_attempt_keeps_active_action_health_unchanged()
    {
        var now = DateTimeOffset.UtcNow;
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), now.AddHours(-1), now.AddHours(1));
        action.MarkStarting();
        action.MarkActive("provider-action", now.AddHours(-1), "active");
        var operation = new ProviderOperation(Guid.NewGuid(), Guid.NewGuid(), action.VisitId, action.Id, ProviderOperationType.Extend);
        operation.SetRequestedEndAt(now.AddHours(2));
        operation.BeginAttempt();

        var changed = ProviderOperationStartupRecovery.MarkStaleInProgressUnknown(
            operation,
            action,
            now.Add(ProviderOperationStartupRecovery.AttemptLease).AddSeconds(1));

        Assert.True(changed);
        Assert.Equal(ProviderOperationStatus.Unknown, operation.Status);
        Assert.Equal(ProviderActionState.Active, action.State);
        Assert.Equal(ProviderActionHealth.Healthy, action.Health);
    }
}
