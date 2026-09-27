using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class StopVisitRequestResolver(ParkerenDbContext dbContext) : IStopVisitRequestResolver
{
    public async Task<StopVisitContext?> ResolveAsync(
        Guid actorUserId,
        Guid visitId,
        CancellationToken cancellationToken = default)
    {
        var actor = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == actorUserId, cancellationToken);
        var visit = await dbContext.Visits
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == visitId, cancellationToken);

        if (actor is null || visit is null)
            return null;

        return new StopVisitContext(
            new StopVisitActor(actor.Id, actor.Role, actor.IsActive),
            visit);
    }
}
