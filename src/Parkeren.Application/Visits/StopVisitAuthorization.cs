using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StopVisitActor(Guid Id, UserRole Role, bool IsActive);

public static class StopVisitAuthorization
{
    public static void Validate(StopVisitActor actor, Visit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        if (!actor.IsActive)
            throw new UnauthorizedAccessException("An active actor is required.");
        if (actor.Id != visit.UserId && actor.Role != UserRole.Admin)
            throw new UnauthorizedAccessException("Only an administrator can stop a Visit for another user.");
    }
}
