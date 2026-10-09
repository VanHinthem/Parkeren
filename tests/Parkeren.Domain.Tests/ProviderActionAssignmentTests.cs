using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderActionAssignmentTests
{
    [Fact]
    public void No_vehicle_users_leaves_historical_action_unassigned()
    {
        var assignment = ProviderActionAssignment.InferFromVehicleUsers([]);

        Assert.Null(assignment.UserId);
        Assert.Equal(ProviderActionAssignmentSource.Unassigned, assignment.Source);
    }

    [Fact]
    public void Exactly_one_distinct_vehicle_user_is_inferred()
    {
        var userId = Guid.NewGuid();

        var assignment = ProviderActionAssignment.InferFromVehicleUsers([userId, userId]);

        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(ProviderActionAssignmentSource.Inferred, assignment.Source);
    }

    [Fact]
    public void Multiple_vehicle_users_leave_historical_action_unassigned()
    {
        var assignment = ProviderActionAssignment.InferFromVehicleUsers([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Null(assignment.UserId);
        Assert.Equal(ProviderActionAssignmentSource.Unassigned, assignment.Source);
    }

    [Fact]
    public void Manual_assignment_is_not_changed_by_history_sync()
    {
        var userId = Guid.NewGuid();
        var assignment = ProviderActionAssignment.Manual(userId);

        var result = assignment.PreserveDuringHistorySync();

        Assert.Equal(userId, result.UserId);
        Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, result.Source);
    }

    [Fact]
    public void Confirmed_assignment_identifies_managed_user()
    {
        var userId = Guid.NewGuid();

        var assignment = ProviderActionAssignment.Confirmed(userId);

        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(ProviderActionAssignmentSource.Confirmed, assignment.Source);
    }

    [Fact]
    public void Assignments_reject_empty_user_ids()
    {
        Assert.Throws<ArgumentException>(() => ProviderActionAssignment.Manual(Guid.Empty));
        Assert.Throws<ArgumentException>(() => ProviderActionAssignment.Confirmed(Guid.Empty));
        Assert.Throws<ArgumentException>(() =>
            ProviderActionAssignment.InferFromVehicleUsers([Guid.Empty]));
    }
}
