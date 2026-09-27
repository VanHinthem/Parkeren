using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class PostgresVisitEndTimeChanger(
    ParkerenDbContext dbContext,
    TimeProvider timeProvider) : IVisitEndTimeChanger
{
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

            await transaction.CommitAsync(cancellationToken);
            return new ChangeVisitEndTimeResult(visit, existing, true);
        }

        if (desiredEndAt is not null)
        {
            var durationValidation = VisitDurationPolicyValidator.Validate(
                visit.PolicySnapshot.ToEffectivePolicy(),
                visit.StartAt,
                desiredEndAt.Value);

            if (!durationValidation.IsAllowed)
                throw new InvalidOperationException("Requested end exceeds the Visit elapsed-duration policy.");
        }

        var change = new VisitEndTimeChange(
            Guid.NewGuid(),
            command.OperationId,
            visit.Id,
            command.ActorUserId,
            visit.DesiredEndAt,
            desiredEndAt,
            timeProvider.GetUtcNow());

        visit.ChangeDesiredEndAt(desiredEndAt);
        change.MarkApplied();
        dbContext.VisitEndTimeChanges.Add(change);

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
