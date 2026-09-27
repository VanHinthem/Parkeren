using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class StopVisitFinalizer(ParkerenDbContext dbContext) : IStopVisitFinalizer
{
    public async Task<bool> RequiresProviderActionAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        return await dbContext.ProviderParkingActions.AnyAsync(
            x => x.VisitId == claim.Visit.Id &&
                 x.State != ProviderActionState.Stopped &&
                 x.State != ProviderActionState.Completed &&
                 x.State != ProviderActionState.Failed,
            cancellationToken);
    }

    public async Task<Visit> CompleteWithoutProviderActionAsync(
        StopVisitClaim claim,
        DateTimeOffset actualEndAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.IsAlreadyCompleted) return claim.Visit;
        if (claim.Operation is null || claim.Operation.Type != ProviderOperationType.Stop)
            throw new InvalidOperationException("A persisted Stop operation is required.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == claim.Visit.Id, cancellationToken);
        var operation = await dbContext.ProviderOperations.SingleAsync(x => x.Id == claim.Operation.Id, cancellationToken);
        var hasOpenProviderAction = await dbContext.ProviderParkingActions.AnyAsync(
            x => x.VisitId == visit.Id &&
                 x.State != ProviderActionState.Stopped &&
                 x.State != ProviderActionState.Completed &&
                 x.State != ProviderActionState.Failed,
            cancellationToken);

        if (hasOpenProviderAction)
            throw new InvalidOperationException("Visit cannot complete locally while a provider action may still require handling.");
        if (visit.Status != VisitStatus.Stopping)
            throw new InvalidOperationException($"Visit must be Stopping before completion, but was {visit.Status}.");
        if (operation.Status != ProviderOperationStatus.Pending)
            throw new InvalidOperationException($"Stop operation must be Pending before provider-free completion, but was {operation.Status}.");

        visit.Complete(actualEndAt);
        operation.BeginAttempt();
        operation.Succeed(actualEndAt);
        dbContext.NotificationEvents.Add(
            new NotificationEvent(Guid.NewGuid(), NotificationEventType.VisitStopped, visit.Id, actualEndAt));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return visit;
    }
}
