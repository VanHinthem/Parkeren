using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderExtendResultStore(ParkerenDbContext dbContext) : IProviderExtendResultStore
{
    public async Task RecordConfirmedAsync(
        ProviderExtendPreparation preparation,
        ProviderAction providerAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(providerAction);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(
            x => x.Id == preparation.Operation.VisitId.Value,
            cancellationToken);
        var action = await dbContext.ProviderParkingActions.SingleAsync(
            x => x.Id == preparation.Action.Id,
            cancellationToken);
        var operation = await dbContext.ProviderOperations.SingleAsync(
            x => x.Id == preparation.Operation.Id,
            cancellationToken);

        if (operation.Status != ProviderOperationStatus.InProgress)
            throw new InvalidOperationException("Provider continuation operation is not in progress.");

        action.ExtendPlannedEnd(providerAction.End);
        operation.Succeed(DateTimeOffset.UtcNow);

        if (visit.Status == VisitStatus.Active)
        {
            visit.SetHealth(VisitHealth.Healthy);

            if (visit.DesiredEndAt is DateTimeOffset desiredEndAt &&
                desiredEndAt > providerAction.End)
            {
                var nextDueAt = providerAction.End;
                var nextWorkExists = await dbContext.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id &&
                         x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                         x.DueAt == nextDueAt,
                    cancellationToken);

                if (!nextWorkExists)
                {
                    dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                        Guid.NewGuid(),
                        visit.Id,
                        VisitSchedulerWorkType.ContinueProviderCoverage,
                        nextDueAt));
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordUnknownAsync(
        ProviderExtendPreparation preparation,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var operation = await dbContext.ProviderOperations.SingleAsync(
            x => x.Id == preparation.Operation.Id,
            cancellationToken);
        if (operation.Status == ProviderOperationStatus.InProgress)
            operation.MarkUnknown(errorCode);

        var visit = await dbContext.Visits.SingleAsync(
            x => x.Id == preparation.Operation.VisitId!.Value,
            cancellationToken);
        if (visit.Status == VisitStatus.Active)
            visit.SetHealth(VisitHealth.Reconciling);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordDefinitiveFailureAsync(
        ProviderExtendPreparation preparation,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var operation = await dbContext.ProviderOperations.SingleAsync(
            x => x.Id == preparation.Operation.Id,
            cancellationToken);
        if (operation.Status == ProviderOperationStatus.InProgress)
            operation.Fail(errorCode, DateTimeOffset.UtcNow);

        var visit = await dbContext.Visits.SingleAsync(
            x => x.Id == preparation.Operation.VisitId!.Value,
            cancellationToken);
        if (visit.Status == VisitStatus.Active)
            visit.SetHealth(VisitHealth.Reconciling);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
