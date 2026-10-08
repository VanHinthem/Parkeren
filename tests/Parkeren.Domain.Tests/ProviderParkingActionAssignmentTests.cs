using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderParkingActionAssignmentTests
{
    [Fact]
    public void Managed_action_stores_vehicle_without_changing_visit_relationship()
    {
        var start = DateTimeOffset.UtcNow;
        var visitId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var action = new ProviderParkingAction(Guid.NewGuid(), visitId,
            start, start.AddHours(1), "visitor", "OSS_J", vehicleId);

        Assert.Equal(visitId, action.VisitId);
        Assert.Equal(vehicleId, action.VehicleId);
        Assert.Equal(ProviderActionOrigin.Managed, action.Origin);
        Assert.Equal(ProviderActionAssignmentSource.Unassigned, action.AssignmentSource);
    }

    [Fact]
    public void Imported_action_captures_vehicle_and_inferred_user_without_visit()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var vehicleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var action = new ProviderParkingAction(Guid.NewGuid(), null, start, start.AddHours(1));

        action.SetImportedAttribution(
            ProviderActionOrigin.Imported,
            vehicleId,
            ProviderActionAssignment.InferFromVehicleUsers([userId]),
            start.AddHours(2));

        Assert.Equal(ProviderActionOrigin.Imported, action.Origin);
        Assert.Null(action.VisitId);
        Assert.Equal(vehicleId, action.VehicleId);
        Assert.Equal(userId, action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.Inferred, action.AssignmentSource);
        Assert.Equal(start.AddHours(2), action.FirstObservedAt);
    }

    [Fact]
    public void Imported_action_can_be_manually_reassigned_and_unassigned()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var action = new ProviderParkingAction(Guid.NewGuid(), null, start, start.AddHours(1));
        action.SetImportedAttribution(ProviderActionOrigin.Imported, Guid.NewGuid(),
            ProviderActionAssignment.Unassigned, start);

        var userId = Guid.NewGuid();
        action.AssignHistoricalUser(ProviderActionAssignment.Manual(userId));
        Assert.Equal(userId, action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, action.AssignmentSource);

        action.RecordHistorySync(start.AddDays(1));
        Assert.Equal(userId, action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, action.AssignmentSource);

        action.AssignHistoricalUser(ProviderActionAssignment.Unassigned);
        Assert.Null(action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.Unassigned, action.AssignmentSource);
    }

    [Fact]
    public void Managed_actions_cannot_be_reclassified_as_imported_or_manually_reassigned()
    {
        var start = DateTimeOffset.UtcNow;
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1));

        Assert.Equal(ProviderActionOrigin.Managed, action.Origin);
        Assert.Throws<InvalidOperationException>(() =>
            action.SetImportedAttribution(ProviderActionOrigin.Imported, Guid.NewGuid(),
                ProviderActionAssignment.Unassigned, start));
        Assert.Throws<InvalidOperationException>(() =>
            action.AssignHistoricalUser(ProviderActionAssignment.Manual(Guid.NewGuid())));
    }
    [Fact]
    public void Import_attribution_cannot_be_initialized_twice_or_overwrite_manual_assignment()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var action = new ProviderParkingAction(Guid.NewGuid(), null, start, start.AddHours(1));
        var vehicleId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        action.SetImportedAttribution(ProviderActionOrigin.Imported, vehicleId,
            ProviderActionAssignment.Unassigned, start);
        action.AssignHistoricalUser(ProviderActionAssignment.Manual(assignedUserId));

        Assert.Throws<InvalidOperationException>(() =>
            action.SetImportedAttribution(ProviderActionOrigin.Imported, Guid.NewGuid(),
                ProviderActionAssignment.Unassigned, start.AddMinutes(1)));

        Assert.Equal(vehicleId, action.VehicleId);
        Assert.Equal(assignedUserId, action.AssignedUserId);
        Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, action.AssignmentSource);
        Assert.Equal(start, action.FirstObservedAt);
    }


}
