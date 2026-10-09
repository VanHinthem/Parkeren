using System.Text.Json;
using Parkeren.Domain.Administration;

namespace Parkeren.Domain.Visits;

/// <summary>
/// Creates an append-only admin audit event for explicit provider-action user attribution.
/// The caller must persist this event in the same transaction as the attribution change.
/// </summary>
public static class ProviderActionAssignmentAudit
{
    public const string ActionName = "ProviderActionUserAssignmentChanged";
    public const string TargetName = "ProviderParkingAction";

    public static AdminAuditEvent Create(
        Guid providerActionId,
        Guid actorUserId,
        ProviderActionAssignment previous,
        ProviderActionAssignment current,
        DateTimeOffset changedAt)
    {
        if (providerActionId == Guid.Empty)
            throw new ArgumentException("Provider action id is required.", nameof(providerActionId));
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous == current)
            throw new ArgumentException("An unchanged assignment must not generate an audit event.", nameof(current));

        var context = JsonSerializer.Serialize(new
        {
            PreviousUserId = previous.UserId,
            PreviousSource = previous.Source.ToString(),
            NewUserId = current.UserId,
            NewSource = current.Source.ToString()
        });

        return new AdminAuditEvent(
            Guid.NewGuid(),
            actorUserId,
            ActionName,
            TargetName,
            providerActionId.ToString("D"),
            changedAt,
            context);
    }
}
