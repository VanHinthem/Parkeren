using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderParkingActionImportTests
{
    [Fact]
    public void Import_completed_history_has_no_visit_and_captures_provider_facts()
    {
        var start = new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(15);
        var vehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), "action-42", "product-1", "OSS Zone J",
            vehicleId, ProviderActionAssignment.InferFromVehicleUsers([userId]),
            start, end, 0.50m, "COMPLETED", end.AddHours(1));

        Assert.Null(action.VisitId);
        Assert.Equal(ProviderActionOrigin.Imported, action.Origin);
        Assert.Equal(ProviderActionState.Completed, action.State);
        Assert.Equal(ProviderHistoryStatus.Reconciled, action.HistoryStatus);
        Assert.Equal("action-42", action.ProviderActionId);
        Assert.Equal("product-1", action.ProviderProductId);
        Assert.Equal("OSS Zone J", action.ProviderLocation);
        Assert.Equal(start, action.ActualStartAt);
        Assert.Equal(end, action.ActualEndAt);
        Assert.Equal(0.50m, action.ProviderCostAmount);
        Assert.Equal("COMPLETED", action.ProviderStatus);
        Assert.Equal(vehicleId, action.VehicleId);
        Assert.Equal(userId, action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.Inferred, action.AssignmentSource);
        Assert.Equal(end.AddHours(1), action.LastSyncedAt);
    }

    [Fact]
    public void Import_without_vehicle_keeps_completed_history_and_remains_unassigned()
    {
        var start = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(30);

        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), "action-no-plate", "product-1", "OSS_J",
            null, ProviderActionAssignment.Unassigned,
            start, end, 0.75m, "COMPLETED", end.AddMinutes(1));

        Assert.Null(action.VehicleId);
        Assert.Null(action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.Unassigned, action.AssignmentSource);
        Assert.Null(action.VisitId);
        Assert.Equal(ProviderActionOrigin.Imported, action.Origin);
        Assert.Equal(ProviderActionState.Completed, action.State);
        Assert.Equal(start, action.ActualStartAt);
        Assert.Equal(end, action.ActualEndAt);
        Assert.Equal(0.75m, action.ProviderCostAmount);
    }

    [Fact]
    public void Import_without_vehicle_cannot_infer_user()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var user = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), "action-invalid-attribution", "product-1", null,
            null, ProviderActionAssignment.InferFromVehicleUsers([user]),
            start, start.AddMinutes(10), 0.25m, "COMPLETED", start.AddHours(1)));
    }

    [Fact]
    public void Import_without_cost_remains_incomplete()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), "action-43", "product-1", null,
            Guid.NewGuid(), ProviderActionAssignment.Unassigned,
            start, start.AddMinutes(1), null, "COMPLETED", DateTimeOffset.UtcNow);

        Assert.Equal(ProviderHistoryStatus.Incomplete, action.HistoryStatus);
        Assert.Null(action.AssignedUserId);
        Assert.Null(action.ProviderCostAmount);
    }

    [Fact]
    public void Import_rejects_negative_cost_and_reversed_interval()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        ProviderParkingAction Make(DateTimeOffset end, decimal? cost) =>
            ProviderParkingAction.ImportCompleted(
                Guid.NewGuid(), "action-44", "product-1", null,
                Guid.NewGuid(), ProviderActionAssignment.Unassigned,
                start, end, cost, "COMPLETED", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => Make(start.AddMinutes(-1), 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(start.AddMinutes(1), -0.01m));
    }

    [Fact]
    public void Import_does_not_allow_lifecycle_mutations()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var action = ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), "action-45", "product-1", null,
            Guid.NewGuid(), ProviderActionAssignment.Unassigned,
            start, start.AddMinutes(1), 0m, "COMPLETED", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => action.MarkStarting());
        Assert.Throws<InvalidOperationException>(() => action.BeginStopping());
    }
}
