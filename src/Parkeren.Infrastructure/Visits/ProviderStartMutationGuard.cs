using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStartMutationGuard(ParkerenDbContext dbContext) : IProviderStartMutationGuard
{
    public async Task<bool> CanStartAsync(Guid visitId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockKey = VisitAdvisoryLock.For(visitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var canStart = await dbContext.Visits
            .AsNoTracking()
            .AnyAsync(x => x.Id == visitId && x.Status == VisitStatus.Starting, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return canStart;
    }
}
