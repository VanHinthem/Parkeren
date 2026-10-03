using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Notifications;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class BudgetWarningService(
    ParkerenDbContext dbContext,
    NotificationInboxWriter inboxWriter)
{
    public async Task EvaluateAsync(
        Visit completedVisit,
        CancellationToken cancellationToken = default,
        ProviderParkingAction? providerActionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(completedVisit);
        if (completedVisit.Status != VisitStatus.Completed || completedVisit.ActualEndAt is null)
            throw new InvalidOperationException("Budget warnings can only be evaluated for a completed Visit.");
        if (providerActionOverride is not null && providerActionOverride.VisitId != completedVisit.Id)
            throw new ArgumentException("Provider action override must belong to the completed Visit.", nameof(providerActionOverride));

        var visitProviderActions = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId == completedVisit.Id)
            .ToListAsync(cancellationToken);
        if (providerActionOverride is not null)
        {
            var actionIndex = visitProviderActions.FindIndex(x => x.Id == providerActionOverride.Id);
            if (actionIndex >= 0)
                visitProviderActions[actionIndex] = providerActionOverride;
            else
                visitProviderActions.Add(providerActionOverride);
        }

        var allPeriods = await dbContext.ParkingBudgetPeriods.AsNoTracking()
            .Where(x => x.ProviderProductId == completedVisit.ProviderProductId)
            .ToListAsync(cancellationToken);
        var periods = allPeriods
            .Where(period =>
                period.ValidFrom < completedVisit.ActualEndAt.Value &&
                period.ValidUntil > completedVisit.StartAt ||
                visitProviderActions.Any(action =>
                    action.ActualStartAt.HasValue &&
                    action.ActualEndAt.HasValue &&
                    period.ValidFrom < action.ActualEndAt.Value &&
                    period.ValidUntil > action.ActualStartAt.Value))
            .OrderBy(x => x.ValidFrom)
            .ToList();

        if (periods.Count == 0)
            return;

        var settings = await dbContext.ParkingSystemSettings.AsNoTracking().SingleAsync(cancellationToken);
        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ProviderProductId == completedVisit.ProviderProductId)
            .ToListAsync(cancellationToken);

        foreach (var period in periods)
        {
            var visits = await dbContext.Visits.AsNoTracking()
                .Where(x => x.ProviderProductId == period.ProviderProductId &&
                            x.Status == VisitStatus.Completed &&
                            x.ActualEndAt.HasValue &&
                            x.Id != completedVisit.Id &&
                            ((x.StartAt < period.ValidUntil && x.ActualEndAt.Value > period.ValidFrom) ||
                             dbContext.ProviderParkingActions.Any(action =>
                                 action.VisitId == x.Id &&
                                 action.ActualStartAt.HasValue &&
                                 action.ActualEndAt.HasValue &&
                                 action.ActualStartAt.Value < period.ValidUntil &&
                                 action.ActualEndAt.Value > period.ValidFrom)))
                .ToListAsync(cancellationToken);
            visits.Add(completedVisit);

            var visitIds = visits.Select(x => x.Id).ToArray();
            var providerActions = await dbContext.ProviderParkingActions.AsNoTracking()
                .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
                .ToListAsync(cancellationToken);
            if (providerActionOverride is not null)
            {
                var actionIndex = providerActions.FindIndex(x => x.Id == providerActionOverride.Id);
                if (actionIndex >= 0)
                    providerActions[actionIndex] = providerActionOverride;
                else
                    providerActions.Add(providerActionOverride);
            }

            var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, visits, providerActions, ruleSets);
            var alreadyNotified = await dbContext.ParkingBudgetWarningStates
                .Where(x => x.ParkingBudgetPeriodId == period.Id)
                .Select(x => x.ThresholdPercentage)
                .ToListAsync(cancellationToken);
            var reached = ParkingBudgetWarningEvaluator.GetNewlyReachedThresholds(
                usage,
                settings.BudgetWarningThresholdPercentages,
                alreadyNotified);

            foreach (var threshold in reached)
            {
                var occurredAt = completedVisit.ActualEndAt.Value;
                var warningStateId = Guid.NewGuid();
                var notificationEvent = new NotificationEvent(
                    Guid.NewGuid(),
                    NotificationEventType.BudgetWarning,
                    warningStateId,
                    occurredAt);
                dbContext.NotificationEvents.Add(notificationEvent);
                dbContext.ParkingBudgetWarningStates.Add(new ParkingBudgetWarningState(
                    warningStateId, period.Id, threshold, occurredAt));

                var payload = JsonSerializer.Serialize(new BudgetWarningNotificationPayload(
                    threshold,
                    usage.UsedPaidDuration,
                    usage.RemainingPaidDuration,
                    period.Id));

                await inboxWriter.WriteAsync(
                    notificationEvent,
                    NotificationType.BudgetWarning,
                    completedVisit.UserId,
                    includeVisitor: false,
                    includeAdmins: true,
                    cancellationToken,
                    payload,
                    completedVisit.Id);
            }
        }
    }
}

internal sealed record BudgetWarningNotificationPayload(
    int ThresholdPercentage,
    TimeSpan UsedPaidDuration,
    TimeSpan RemainingPaidDuration,
    Guid ParkingBudgetPeriodId);
