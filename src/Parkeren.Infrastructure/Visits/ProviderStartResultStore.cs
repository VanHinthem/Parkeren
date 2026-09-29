using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Infrastructure.Visits;

internal sealed class ProviderStartResultStore(ParkerenDbContext dbContext) : IProviderStartResultStore
{
    public async Task RecordResponseAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        ArgumentNullException.ThrowIfNull(providerAction);
        preparation.Action.CaptureStartResponse(providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordRetryableAsync(ProviderStartPreparation preparation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        preparation.Action.ResetForRetry();
        preparation.Operation.ResetForRetry();
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        visit.SetHealth(VisitHealth.Healthy);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordConfirmedAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        ArgumentNullException.ThrowIfNull(providerAction);
        preparation.Action.MarkActive(providerAction.ProviderActionId, providerAction.Start, providerAction.Status);
        preparation.Operation.Succeed(DateTimeOffset.UtcNow);
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");

        // A Stop request can win while the external Start call is in flight.
        // Reload so an already tracked Starting Visit cannot overwrite that Stop claim.
        await dbContext.Entry(visit).ReloadAsync(cancellationToken);
        if (visit.Status == VisitStatus.Starting)
        {
            visit.Activate();
            visit.SetHealth(VisitHealth.Healthy);

            if (visit.PolicySnapshot.AllowVisitExtension &&
                visit.DesiredEndAt is { } desiredEndAt &&
                desiredEndAt > preparation.Action.PlannedEndAt)
            {
                var ruleSets = await dbContext.ParkingRuleSets
                    .Include(x => x.PaidWindows)
                    .Include(x => x.CalendarExceptions)
                    .Where(x => x.ValidFrom < desiredEndAt &&
                                (!x.ValidUntil.HasValue || x.ValidUntil.Value > preparation.Action.PlannedEndAt))
                    .ToListAsync(cancellationToken);
                var nextPaid = ProviderCoverageSchedule.NextPaidSegment(
                    preparation.Action.PlannedEndAt, desiredEndAt, ruleSets);
                if (nextPaid is not null && !await dbContext.VisitSchedulerWork.AnyAsync(
                    work => work.VisitId == visit.Id &&
                            work.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                            (work.Status == VisitSchedulerWorkStatus.Pending ||
                             work.Status == VisitSchedulerWorkStatus.Claimed),
                    cancellationToken))
                {
                    var dueAt = nextPaid.Start > preparation.Action.PlannedEndAt
                        ? nextPaid.Start
                        : ProviderCoverageSchedule.PrecheckAt(preparation.Action.PlannedEndAt);
                    dbContext.VisitSchedulerWork.Add(new VisitSchedulerWork(
                        Guid.NewGuid(), visit.Id, VisitSchedulerWorkType.ContinueProviderCoverage, dueAt));
                }
            }
        }
        else if (visit.Status != VisitStatus.Stopping)
        {
            throw new InvalidOperationException($"Provider Start cannot be confirmed for Visit in state {visit.Status}.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        preparation.Operation.Fail(errorCode, DateTimeOffset.UtcNow);
        preparation.Action.MarkFailed();
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        visit.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(preparation.Operation.VisitId!.Value);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        preparation.Action.MarkUnknown();
        preparation.Operation.MarkUnknown(errorCode);
        var visit = await dbContext.Visits.FindAsync([preparation.Operation.VisitId!.Value], cancellationToken);
        if (visit is null) throw new InvalidOperationException("Visit for provider start operation was not found.");
        visit.SetHealth(VisitHealth.Reconciling);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
