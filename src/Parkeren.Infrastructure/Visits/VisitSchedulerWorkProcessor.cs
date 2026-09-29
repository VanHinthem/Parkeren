using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Visits;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Vehicles;
using Parkeren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Parkeren.Infrastructure.Visits;

internal sealed class VisitSchedulerWorkProcessor(
    ParkerenDbContext dbContext,
    IProviderExtendStore providerExtendStore,
    IProviderContinuationStartStore continuationStartStore,
    IConfiguration configuration,
    IServiceProvider serviceProvider)
    : IVisitSchedulerWorkProcessor
{
    public async Task ProcessAsync(
        VisitSchedulerWork work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (work.Status != VisitSchedulerWorkStatus.Claimed)
            throw new InvalidOperationException("Only claimed scheduler work can be processed.");

        var visit = await dbContext.Visits.SingleAsync(x => x.Id == work.VisitId, cancellationToken);
        if (visit.Status != VisitStatus.Active || visit.Health != VisitHealth.Healthy)
        {
            work.Cancel();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var latestAction = await dbContext.ProviderParkingActions
            .Where(x => x.VisitId == visit.Id && x.State == ProviderActionState.Active)
            .OrderByDescending(x => x.PlannedEndAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestAction is null)
        {
            var lastCompletedEnd = await dbContext.ProviderParkingActions.AsNoTracking()
                .Where(x => x.VisitId == visit.Id && x.State == ProviderActionState.Completed)
                .OrderByDescending(x => x.PlannedEndAt)
                .Select(x => (DateTimeOffset?)x.PlannedEndAt)
                .FirstOrDefaultAsync(cancellationToken);
            await ProcessInitialCoverageAsync(work, visit, lastCompletedEnd, cancellationToken);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (await dbContext.ProviderOperations.AnyAsync(
                x => x.OperationId == work.Id &&
                     x.ProviderParkingActionId == latestAction.Id &&
                     x.Status == ProviderOperationStatus.Succeeded,
                cancellationToken))
        {
            // A previous attempt already confirmed a later provider action.
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        var continuationPrecheckAt = ProviderCoverageSchedule.PrecheckAt(latestAction.PlannedEndAt);
        if (continuationPrecheckAt > now)
        {
            work.Release(continuationPrecheckAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var desiredEndAt = ProviderCoverageSchedule.PlanningEndAt(visit, now);
        if (desiredEndAt <= now ||
            (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan maxElapsed &&
             visit.StartAt + maxElapsed <= now))
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan remainingMaxElapsed)
        {
            var hardEndAt = visit.StartAt + remainingMaxElapsed;
            if (desiredEndAt > hardEndAt)
                desiredEndAt = hardEndAt;

            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var ruleSets = await dbContext.ParkingRuleSets
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < desiredEndAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
            .ToListAsync(cancellationToken);

        var paidThroughNow = ParkingRuleSetPaidTimeCalculator.Calculate(
            visit.StartAt,
            now,
            ruleSets);

        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaidParkingDuration &&
            paidThroughNow >= maxPaidParkingDuration)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var paidThroughDesiredEnd = ParkingRuleSetPaidTimeCalculator.Calculate(
            visit.StartAt,
            desiredEndAt,
            ruleSets);

        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaidParkingDurationForDesiredEnd &&
            paidThroughDesiredEnd > maxPaidParkingDurationForDesiredEnd)
        {
            desiredEndAt = FindPaidDurationBoundary(
                visit.StartAt,
                desiredEndAt,
                maxPaidParkingDurationForDesiredEnd,
                ruleSets);

            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var nextPaid = ProviderCoverageSchedule.NextPaidSegment(
            latestAction.PlannedEndAt, desiredEndAt, ruleSets);
        if (nextPaid is null)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (nextPaid.Start > latestAction.PlannedEndAt)
        {
            if (nextPaid.Start > now)
            {
                work.Release(nextPaid.Start);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var parkingProvider = serviceProvider.GetRequiredService<IParkingProvider>();
            var remoteActions = await parkingProvider.GetActionsAsync(cancellationToken);
            var previous = remoteActions.SingleOrDefault(x => x.ProviderActionId == latestAction.ProviderActionId);
            if (previous is null ||
                !string.Equals(previous.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                (previous.End - latestAction.PlannedEndAt).Duration() >= TimeSpan.FromMilliseconds(1))
            {
                if (previous?.Status is { } status &&
                    string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase))
                    latestAction.MarkExternallyStopped(status);
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            latestAction.MarkCompleted(latestAction.PlannedEndAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            await ProcessInitialCoverageAsync(work, visit, nextPaid.Start, cancellationToken);
            return;
        }

        var actionRules = ruleSets
            .Where(x => x.ValidFrom <= latestAction.PlannedEndAt &&
                        (x.ValidUntil is null || x.ValidUntil > latestAction.PlannedEndAt))
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefault();
        if (actionRules is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (actionRules.Continuation == ProviderCoverageContinuation.StartNewAction)
        {
            var parkingProvider = serviceProvider.GetService<IParkingProvider>();
            if (parkingProvider is null || string.IsNullOrWhiteSpace(latestAction.ProviderActionId))
            {
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var providerActions = await parkingProvider.GetActionsAsync(cancellationToken);
            var previousAtProvider = providerActions.SingleOrDefault(x =>
                x.ProviderActionId == latestAction.ProviderActionId);
            if (previousAtProvider is null ||
                !string.Equals(previousAtProvider.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                (previousAtProvider.End - latestAction.PlannedEndAt).Duration() >= TimeSpan.FromMilliseconds(1))
            {
                if (previousAtProvider?.Status is { } providerStatus &&
                    string.Equals(providerStatus, "stopped", StringComparison.OrdinalIgnoreCase))
                    latestAction.MarkExternallyStopped(providerStatus);
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var existing = await dbContext.ProviderOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationId == work.Id, cancellationToken);
            var nextEndAt = existing?.ProviderParkingActionId is Guid existingActionId
                ? (await dbContext.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.Id == existingActionId, cancellationToken)).PlannedEndAt
                : new[] { desiredEndAt, latestAction.PlannedEndAt + actionRules.MaxProviderActionDuration, nextPaid.End }.Min();

            var location = configuration["ParkingProvider:Location"];
            var executor = serviceProvider.GetService<ContinueVisitStartExecutor>();
            if (string.IsNullOrWhiteSpace(location) || executor is null)
            {
                work.Release(now.AddMinutes(1));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var licensePlate = await dbContext.Vehicles.AsNoTracking()
                .Where(x => x.Id == visit.VehicleId)
                .Select(x => x.LicensePlate)
                .SingleAsync(cancellationToken);
            var startPreparation = await continuationStartStore.PrepareAttemptAsync(
                visit, latestAction, work.Id, nextEndAt, cancellationToken);
            var startExecution = await executor.ExecuteAsync(
                startPreparation, licensePlate, location, cancellationToken);
            if (startExecution.RequiresReconciliation)
            {
                work.Release(now.AddMinutes(1));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var maxExistingEndAt = latestAction.PlannedStartAt + actionRules.MaxProviderActionDuration;
        if (desiredEndAt > maxExistingEndAt)
            desiredEndAt = maxExistingEndAt;
        if (desiredEndAt > nextPaid.End)
            desiredEndAt = nextPaid.End;
        if (desiredEndAt <= latestAction.PlannedEndAt)
        {
            visit.SetHealth(VisitHealth.Reconciling);
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var providerExtendExecutor = serviceProvider.GetService<ContinueVisitProviderExecutor>();
        if (providerExtendExecutor is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var preparation = await providerExtendStore.PrepareAttemptAsync(
            visit,
            latestAction,
            work.Id,
            desiredEndAt,
            cancellationToken);

        var execution = await providerExtendExecutor.ExecuteAsync(preparation, cancellationToken);
        if (execution.RequiresReconciliation)
        {
            if (preparation.Operation.Status == ProviderOperationStatus.Unknown)
            {
                var reconciler = serviceProvider.GetService<ContinueVisitProviderReconciler>();
                if (reconciler is not null)
                {
                    var reconciled = await reconciler.ReconcileAsync(preparation, cancellationToken);
                    if (reconciled is not null)
                    {
                        work.Complete(now);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return;
                    }
                }
            }

            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (execution.DefinitiveFailure)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessInitialCoverageAsync(
        VisitSchedulerWork work, Visit visit, DateTimeOffset? fromAt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var desiredEndAt = ProviderCoverageSchedule.PlanningEndAt(visit, now);
        if (desiredEndAt <= now)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (visit.PolicySnapshot.MaxVisitElapsedDuration is TimeSpan maxElapsed)
        {
            var hardEnd = visit.StartAt + maxElapsed;
            if (hardEnd < desiredEndAt)
                desiredEndAt = hardEnd;
            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var rules = await dbContext.ParkingRuleSets
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < desiredEndAt &&
                        (!x.ValidUntil.HasValue || x.ValidUntil.Value > visit.StartAt))
            .ToListAsync(cancellationToken);
        if (visit.PolicySnapshot.MaxPaidParkingDuration is TimeSpan maxPaidParkingDurationForInitialCoverage &&
            ParkingRuleSetPaidTimeCalculator.Calculate(visit.StartAt, desiredEndAt, rules) > maxPaidParkingDurationForInitialCoverage)
        {
            desiredEndAt = FindPaidDurationBoundary(
                visit.StartAt, desiredEndAt, maxPaidParkingDurationForInitialCoverage, rules);
            if (desiredEndAt <= now)
            {
                work.Complete(now);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }
        var paid = ProviderCoverageSchedule.NextPaidSegment(fromAt ?? visit.StartAt, desiredEndAt, rules);
        if (paid is null)
        {
            work.Complete(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (paid.Start > now)
        {
            work.Release(paid.Start);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var actionRules = rules.Single(x => x.ValidFrom <= paid.Start &&
            (x.ValidUntil is null || x.ValidUntil > paid.Start));
        var endAt = paid.End < paid.Start + actionRules.MaxProviderActionDuration
            ? paid.End : paid.Start + actionRules.MaxProviderActionDuration;
        var location = configuration["ParkingProvider:Location"];
        var executor = serviceProvider.GetService<ContinueVisitStartExecutor>();
        if (string.IsNullOrWhiteSpace(location) || executor is null)
        {
            work.Release(now.AddMinutes(1));
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var licensePlate = await dbContext.Vehicles.AsNoTracking()
            .Where(x => x.Id == visit.VehicleId)
            .Select(x => x.LicensePlate)
            .SingleAsync(cancellationToken);
        if (!await dbContext.ProviderOperations.AnyAsync(x => x.OperationId == work.Id, cancellationToken))
        {
            var parkingProvider = serviceProvider.GetRequiredService<IParkingProvider>();
            var remoteActions = await parkingProvider.GetActionsAsync(cancellationToken);
            if (remoteActions.Any(x =>
                    Vehicle.NormalizeLicensePlate(x.LicensePlate) == Vehicle.NormalizeLicensePlate(licensePlate) &&
                    (x.Start - paid.Start).Duration() < TimeSpan.FromMilliseconds(1)))
            {
                visit.SetHealth(VisitHealth.AttentionRequired);
                work.Cancel();
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }
        var preparation = await continuationStartStore.PrepareInitialCoverageAsync(
            visit, work.Id, paid.Start, endAt, cancellationToken);
        var execution = await executor.ExecuteAsync(preparation, licensePlate, location, cancellationToken);
        if (execution.RequiresReconciliation)
            work.Release(now.AddMinutes(1));
        else
            work.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset FindPaidDurationBoundary(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan maxPaidDuration,
        IReadOnlyCollection<ParkingRuleSet> ruleSets)
    {
        var periods = ParkingRuleSetPeriodSegmenter.Segment(start, end, ruleSets);
        var paid = TimeSpan.Zero;

        foreach (var segment in periods
                     .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
                     .OrderBy(x => x.Start))
        {
            if (!segment.IsPaid)
                continue;

            var duration = segment.End - segment.Start;
            if (paid + duration >= maxPaidDuration)
                return segment.Start + (maxPaidDuration - paid);

            paid += duration;
        }

        return end;
    }
}
