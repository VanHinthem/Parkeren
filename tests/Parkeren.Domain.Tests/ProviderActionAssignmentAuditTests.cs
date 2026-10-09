using System.Text.Json;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderActionAssignmentAuditTests
{
    [Fact]
    public void Manual_assignment_audit_records_before_after_and_actor()
    {
        var actionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var priorUser = Guid.NewGuid();
        var newUser = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var audit = ProviderActionAssignmentAudit.Create(
            actionId,
            actorId,
            ProviderActionAssignment.InferFromVehicleUsers([priorUser]),
            ProviderActionAssignment.Manual(newUser),
            timestamp);

        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Equal(timestamp, audit.CreatedAt);
        Assert.Equal(ProviderActionAssignmentAudit.ActionName, audit.Action);
        Assert.Equal(ProviderActionAssignmentAudit.TargetName, audit.TargetType);
        Assert.Equal(actionId.ToString("D"), audit.TargetId);
        using var data = JsonDocument.Parse(audit.ContextJson!);
        Assert.Equal(priorUser.ToString("D"), data.RootElement.GetProperty("PreviousUserId").GetString());
        Assert.Equal("Inferred", data.RootElement.GetProperty("PreviousSource").GetString());
        Assert.Equal(newUser.ToString("D"), data.RootElement.GetProperty("NewUserId").GetString());
        Assert.Equal("ManuallyAssigned", data.RootElement.GetProperty("NewSource").GetString());
    }

    [Fact]
    public void Explicit_unassignment_is_audited()
    {
        var audit = ProviderActionAssignmentAudit.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderActionAssignment.Manual(Guid.NewGuid()),
            ProviderActionAssignment.Unassigned,
            DateTimeOffset.UtcNow);

        using var data = JsonDocument.Parse(audit.ContextJson!);
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("NewUserId").ValueKind);
        Assert.Equal("Unassigned", data.RootElement.GetProperty("NewSource").GetString());
    }

    [Fact]
    public void Unchanged_assignment_does_not_create_audit()
    {
        var assignment = ProviderActionAssignment.Manual(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => ProviderActionAssignmentAudit.Create(
            Guid.NewGuid(), Guid.NewGuid(), assignment, assignment, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Empty_action_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ProviderActionAssignmentAudit.Create(
            Guid.Empty, Guid.NewGuid(), ProviderActionAssignment.Unassigned,
            ProviderActionAssignment.Manual(Guid.NewGuid()), DateTimeOffset.UtcNow));
    }
}
