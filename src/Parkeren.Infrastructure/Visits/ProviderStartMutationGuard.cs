using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStartMutationGuard(ParkerenDbContext dbContext) : IProviderStartMutationGuard
{
    public async Task<bool> CanStartAsync(Guid visitId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Visits
            .AsNoTracking()
            .AnyAsync(x => x.Id == visitId && x.Status == VisitStatus.Starting, cancellationToken);
    }
}
