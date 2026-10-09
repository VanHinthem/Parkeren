using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Explicit admin attribution of imported provider actions.
/// The assignment and its audit entry are saved atomically.
/// </summary>
public sealed class ProviderHistoryAssignmentService(ParkerenDbContext db, TimeProvider clock)
{
    public async Task<bool> AssignAsync(
        Guid actionId, Guid actorUserId, Guid? assignedUserId,
        CancellationToken cancellationToken = default)
    {
        if (actionId == Guid.Empty) throw new ArgumentException("Action id is required.", nameof(actionId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorUserId));

        var action = await db.ProviderParkingActions.SingleAsync(x => x.Id == actionId, cancellationToken);
        if (action.Origin == ProviderActionOrigin.Managed)
            throw new InvalidOperationException("Managed provider action attribution cannot be changed.");

        var isAdmin = await db.Users.AnyAsync(
            x => x.Id == actorUserId && x.Role == UserRole.Admin &&
                 x.Status == UserStatus.Active, cancellationToken);
        if (!isAdmin)
            throw new InvalidOperationException("An active administrator is required to assign historical actions.");

        if (assignedUserId.HasValue && !await db.Users.AnyAsync(x => x.Id == assignedUserId.Value, cancellationToken))
            throw new InvalidOperationException("Assigned user does not exist.");

        var previous = new ProviderActionAssignment(action.AssignedUserId, action.AssignmentSource);
        var current = assignedUserId.HasValue
            ? ProviderActionAssignment.Manual(assignedUserId.Value)
            : ProviderActionAssignment.Unassigned;

        if (previous == current)
            return false;

        action.AssignHistoricalUser(current);
        db.AdminAuditEvents.Add(ProviderActionAssignmentAudit.Create(
            actionId, actorUserId, previous, current, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
