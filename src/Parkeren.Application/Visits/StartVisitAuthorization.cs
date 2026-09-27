using Parkeren.Domain.Users;

namespace Parkeren.Application.Visits;

public sealed record StartVisitActor(Guid Id, UserRole Role, bool IsActive);
public sealed record StartVisitOwner(Guid Id, bool IsActive);
public sealed record StartVisitVehicle(Guid Id, bool IsActive, bool IsAssignedToOwner);

public static class StartVisitAuthorization
{
    public static void Validate(StartVisitActor actor, StartVisitOwner owner, StartVisitVehicle vehicle)
    {
        if (!actor.IsActive || !owner.IsActive) throw new UnauthorizedAccessException("Active actor and Visit owner are required.");
        if (actor.Id != owner.Id && actor.Role != UserRole.Admin) throw new UnauthorizedAccessException("Only an administrator can start a Visit for another user.");
        if (!vehicle.IsActive) throw new InvalidOperationException("Vehicle is not active.");
        if (!vehicle.IsAssignedToOwner) throw new UnauthorizedAccessException("Vehicle is not assigned to the Visit owner.");
    }
}
