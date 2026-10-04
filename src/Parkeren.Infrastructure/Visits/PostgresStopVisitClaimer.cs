using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresStopVisitClaimer(ParkerenDbContext dbContext) : IStopVisitClaimer
{
    public async Task<StopVisitClaim> ClaimAsync(StopVisitCommand command, CancellationToken cancellationToken = default)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(command));
        if (command.VisitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(command));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(command.VisitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == command.VisitId, cancellationToken);
        var endReason = command.EndReason ?? await dbContext.VisitSchedulerWork
            .AsNoTracking()
            .Where(x => x.Id == command.OperationId &&
                        x.VisitId == visit.Id &&
                        x.Type == VisitSchedulerWorkType.StopVisit)
            .Select(x => x.EndReason)
            .SingleOrDefaultAsync(cancellationToken);
        var existing = await dbContext.ProviderOperations.SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Type != ProviderOperationType.Stop || existing.VisitId != visit.Id)
                throw new InvalidOperationException("Operation id is already used by another provider operation.");
            EnsureEndReasonMatches(visit, endReason);
            await transaction.CommitAsync(cancellationToken);
            return new StopVisitClaim(visit, existing, true, visit.Status is VisitStatus.Completed or VisitStatus.Cancelled);
        }

        if (visit.Status is VisitStatus.Completed or VisitStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopVisitClaim(visit, null, false, true);
        }

        if (visit.Status == VisitStatus.Stopping)
        {
            EnsureEndReasonMatches(visit, endReason);
            var activeStop = await dbContext.ProviderOperations
                .Where(x => x.VisitId == visit.Id &&
                            x.Type == ProviderOperationType.Stop &&
                            x.Status != ProviderOperationStatus.Succeeded &&
                            x.Status != ProviderOperationStatus.Failed)
                .OrderBy(x => x.CreatedAt)
                .SingleOrDefaultAsync(cancellationToken);

            if (activeStop is null)
                throw new InvalidOperationException("Stopping Visit has no active Stop operation.");

            await transaction.CommitAsync(cancellationToken);
            return new StopVisitClaim(visit, activeStop, true, false);
        }

        visit.BeginStopping(endReason ?? VisitEndReason.ManualStop);

        var pendingSchedulerWork = await dbContext.VisitSchedulerWork
            .Where(x => x.VisitId == visit.Id &&
                        x.Status == VisitSchedulerWorkStatus.Pending &&
                        x.Type != VisitSchedulerWorkType.ReconcileProviderAction)
            .ToListAsync(cancellationToken);
        foreach (var work in pendingSchedulerWork)
            work.Cancel("visit_entered_stopping");

        var operation = new ProviderOperation(Guid.NewGuid(), command.OperationId, visit.Id, null, ProviderOperationType.Stop);
        dbContext.ProviderOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StopVisitClaim(visit, operation, false, false);
    }

    private static void EnsureEndReasonMatches(Visit visit, VisitEndReason? requestedEndReason)
    {
        if (requestedEndReason is not null &&
            visit.EndReason is not null &&
            visit.EndReason != requestedEndReason)
            throw new InvalidOperationException("Stop replay end reason does not match the Visit end reason.");
    }
}
