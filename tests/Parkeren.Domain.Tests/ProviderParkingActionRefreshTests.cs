using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderParkingActionRefreshTests
{
    [Fact]
    public void Refresh_changes_provider_facts_but_preserves_manual_assignment()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var vehicle = Guid.NewGuid();
        var action = ProviderParkingAction.ImportCompleted(Guid.NewGuid(), "a-1", "product-1",
            "OSS Zone J", vehicle, ProviderActionAssignment.Unassigned,
            at, at.AddMinutes(10), 0.10m, "COMPLETED", at.AddHours(1));
        var userId = Guid.NewGuid();
        action.AssignHistoricalUser(ProviderActionAssignment.Manual(userId));

        action.RefreshImportedHistory(at.AddSeconds(3), at.AddMinutes(11),
            0.12m, "COMPLETED", at.AddHours(2));

        Assert.Equal(at.AddSeconds(3), action.ActualStartAt);
        Assert.Equal(at.AddMinutes(11), action.ActualEndAt);
        Assert.Equal(0.12m, action.ProviderCostAmount);
        Assert.Equal(userId, action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, action.AssignmentSource);
        Assert.Equal(vehicle, action.VehicleId);
        Assert.Null(action.VisitId);
        Assert.Equal(ProviderActionState.Completed, action.State);
    }

    [Fact]
    public void Refresh_missing_cost_marks_history_incomplete()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var action = ProviderParkingAction.ImportCompleted(Guid.NewGuid(), "a-2", "product-1",
            null, Guid.NewGuid(), ProviderActionAssignment.Unassigned,
            at, at.AddMinutes(10), 0.10m, "COMPLETED", at.AddHours(1));

        action.RefreshImportedHistory(at, at.AddMinutes(10), null, "COMPLETED", at.AddHours(2));

        Assert.Null(action.ProviderCostAmount);
        Assert.Equal(ProviderHistoryStatus.Incomplete, action.HistoryStatus);
    }

    [Fact]
    public void Managed_actions_cannot_be_refreshed_through_import_path()
    {
        var at = DateTimeOffset.UtcNow.AddHours(-2);
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), at, at.AddHours(1));
        action.MarkStarting();
        action.MarkActive("managed-1", at);
        action.MarkCompleted(at.AddHours(1));

        Assert.Throws<InvalidOperationException>(() =>
            action.RefreshImportedHistory(at, at.AddHours(1), 0.1m, "COMPLETED", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Invalid_refresh_does_not_change_imported_data()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var action = ProviderParkingAction.ImportCompleted(Guid.NewGuid(), "a-3", "product-1",
            null, Guid.NewGuid(), ProviderActionAssignment.Unassigned,
            at, at.AddMinutes(10), 0.10m, "COMPLETED", at.AddHours(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            action.RefreshImportedHistory(at, at.AddMinutes(-1), 0.20m, "COMPLETED", at.AddHours(2)));
        Assert.Equal(0.10m, action.ProviderCostAmount);
        Assert.Equal(at.AddMinutes(10), action.ActualEndAt);
    }

    [Fact]
    public void Stale_sync_cannot_overwrite_newer_observation()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var action = ProviderParkingAction.ImportCompleted(Guid.NewGuid(), "a-4", "product-1",
            null, Guid.NewGuid(), ProviderActionAssignment.Unassigned,
            at, at.AddMinutes(10), 0.10m, "COMPLETED", at.AddHours(1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            action.RefreshImportedHistory(at, at.AddMinutes(10), 0.20m, "COMPLETED", at));
        Assert.Equal(0.10m, action.ProviderCostAmount);
    }
}
