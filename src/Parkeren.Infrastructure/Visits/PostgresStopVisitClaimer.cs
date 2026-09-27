using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresStopVisitClaimer(ParkerenDbContext dbContext) : IStopVisitClaimer
{
    private const long StopVisitLockNamespace = 0x53544F50; // STOP

    public async Task<StopVisitClaim> ClaimAsync(StopVisitCommand command, CancellationToken cancellationToken = default)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(command));
        if (command.VisitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(command));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = StopVisitLockNamespace ^ command.VisitId.GetHashCode();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == command.VisitId, cancellationToken);
        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Type != ProviderOperationType.Stop || existing.VisitId != visit.Id)
                throw new InvalidOperationException("Operation id is already used by another provider operation.");
            await transaction.CommitAsync(cancellationToken);
            return new StopVisitClaim(visit, existing, true, visit.Status is VisitStatus.Completed or VisitStatus.Cancelled);
        }

        if (visit.Status is VisitStatus.Completed or VisitStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopVisitClaim(visit, null, false, true);
        }

        visit.BeginStopping();
        var operation = new ProviderOperation(Guid.NewGuid(), command.OperationId, visit.Id, null, ProviderOperationType.Stop);
        dbContext.ProviderOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StopVisitClaim(visit, operation, false, false);
    }
}
