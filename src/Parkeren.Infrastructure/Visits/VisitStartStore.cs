using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitStartStore(ParkerenDbContext dbContext) : IVisitStartStore
{
    public async Task SaveAsync(Visit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
