using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitEndTimeChanger(
    ParkerenDbContext dbContext,
    TimeProvider timeProvider,
    IChangeVisitEndTimeOperationalContextResolver operationalContextResolver) : IVisitEndTimeChanger
{
    public async Task<ChangeVisitEndTimeResult> PrepareAsync(
        ChangeVisitEndTimeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(command));
        if (command.VisitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(command));
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(command));

        var desiredEndAt = NormalizeTimestamp(command.DesiredEndAt);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(command.VisitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == command.VisitId, cancellationToken);
        var existing = await dbContext.VisitEndTimeChanges
            .SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.VisitId != visit.Id ||
                existing.ActorUserId != command.ActorUserId ||
                existing.RequestedDesiredEndAt != desiredEndAt)
                throw new InvalidOperationException("Operation id is already used by another end-time change.");
            await transaction.CommitAsync(cancellationToken);
            return new ChangeVisitEndTimeResult(visit, existing, true);
        }

        visit.EnsureDesiredEndCanChange(desiredEndAt);
        var change = new VisitEndTimeChange(
            Guid.NewGuid(), command.OperationId, visit.Id, command.ActorUserId,
            visit.DesiredEndAt, desiredEndAt, timeProvider.GetUtcNow());
        dbContext.VisitEndTimeChanges.Add(change);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChangeVisitEndTimeResult(visit, change, false);
    }

    public async Task<ChangeVisitEndTimeResult> ApplyAsync(
        ChangeVisitEndTimeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OperationId == Guid.Empty) throw new ArgumentException("Operation id is required.", nameof(command));
        if (command.VisitId == Guid.Empty) throw new ArgumentException("Visit id is required.", nameof(command));
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(command));

        var desiredEndAt = NormalizeTimestamp(command.DesiredEndAt);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = VisitAdvisoryLock.For(command.VisitId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == command.VisitId, cancellationToken);
        var existing = await dbContext.VisitEndTimeChanges
            .SingleOrDefaultAsync(x => x.OperationId == command.OperationId, cancellationToken);

        if (existing is not null)
        {
            if (existing.VisitId != visit.Id ||
                existing.ActorUserId != command.ActorUserId ||
                existing.RequestedDesiredEndAt != desiredEndAt)
                throw new InvalidOperationException("Operation id is already used by another end-time change.");

            if (existing.Result == VisitEndTimeChangeResult.Rejected)
            {
                await transaction.CommitAsync(cancellationToken);
                throw new InvalidOperationException("End-time change was previously rejected.");
            }
            if (existing.Result == VisitEndTimeChangeResult.Applied)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ChangeVisitEndTimeResult(visit, existing, true);
            }
        }

        var change = existing ?? new VisitEndTimeChange(
            Guid.NewGuid(),
            command.OperationId,
            visit.Id,
            command.ActorUserId,
            visit.DesiredEndAt,
            desiredEndAt,
            timeProvider.GetUtcNow());

        try
        {
            visit.EnsureDesiredEndCanChange(desiredEndAt);

            if (await dbContext.VisitSchedulerWork.AnyAsync(
                    x => x.VisitId == visit.Id &&
                         x.Status == VisitSchedulerWorkStatus.Claimed,
                    cancellationToken))
                throw new InvalidOperationException("End-time cannot change while scheduler work is being processed.");

            if (desiredEndAt is not null && visit.DesiredEndAt is not null && desiredEndAt < visit.DesiredEndAt)
            {
                var providerActions = await dbContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .ToListAsync(cancellationToken);

                var affectedActions = providerActions
                    .Where(x => x.PlannedEndAt > desiredEndAt.Value &&
                                x.State is ProviderActionState.Active or ProviderActionState.Scheduled)
                    .ToList();

                if (affectedActions.Any(x => x.State == ProviderActionState.Active))
                    throw new InvalidOperationException(
                        "Requested end shortens an active provider action; that provider mutation strategy is not yet available.");

                if (affectedActions.Any(x => x.State == ProviderActionState.Scheduled))
                    throw new InvalidOperationException(
                        "Requested end affects a scheduled provider action; cancel/recreate must be completed before applying the end-time change.");
            }

            if (desiredEndAt is not null)
            {
                var operationalContext = await operationalContextResolver.ResolveAsync(
                    visit.StartAt,
                    desiredEndAt.Value,
                    cancellationToken);
                if (operationalContext is null)
                    throw new InvalidOperationException("No parking rules apply to the requested Visit period.");

                var assessment = VisitEndTimeChangeAssessor.Assess(
                    visit.StartAt,
                    desiredEndAt.Value,
                    operationalContext.RuleSets,
                    visit.PolicySnapshot.ToEffectivePolicy());

                if (!assessment.PaidDuration.IsAllowed)
                    throw new InvalidOperationException("Requested end exceeds the Visit paid-duration policy.");
                if (!assessment.ElapsedDuration.IsAllowed)
                    throw new InvalidOperationException("Requested end exceeds the Visit elapsed-duration policy.");
            }

            visit.ChangeDesiredEndAt(desiredEndAt);
            if (desiredEndAt is DateTimeOffset newEndAt)
            {
                var obsoleteWork = await dbContext.VisitSchedulerWork
                    .Where(x => x.VisitId == visit.Id &&
                                x.Status == VisitSchedulerWorkStatus.Pending &&
                                x.DueAt >= newEndAt)
                    .ToListAsync(cancellationToken);
                foreach (var work in obsoleteWork)
                    work.Cancel();
            }
            change.MarkApplied();
            if (existing is null)
                dbContext.VisitEndTimeChanges.Add(change);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            change.MarkRejected();
            if (existing is null)
                dbContext.VisitEndTimeChanges.Add(change);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChangeVisitEndTimeResult(visit, change, false);
    }
    private static DateTimeOffset? NormalizeTimestamp(DateTimeOffset? value)
    {
        if (value is null) return null;

        const long ticksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;
        var utcTicks = value.Value.UtcTicks;
        var normalizedTicks = utcTicks - (utcTicks % ticksPerMicrosecond);
        return new DateTimeOffset(normalizedTicks, TimeSpan.Zero);
    }
}
