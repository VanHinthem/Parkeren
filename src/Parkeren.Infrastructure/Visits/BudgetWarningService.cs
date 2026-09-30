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
    public async Task EvaluateAsync(Visit completedVisit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedVisit);
        if (completedVisit.Status != VisitStatus.Completed || completedVisit.ActualEndAt is null)
            throw new InvalidOperationException("Budget warnings can only be evaluated for a completed Visit.");

        var periods = await dbContext.ParkingBudgetPeriods
            .AsNoTracking()
            .Where(x => x.ValidFrom < completedVisit.ActualEndAt.Value && x.ValidUntil > completedVisit.StartAt)
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        if (periods.Count == 0)
            return;

        var settings = await dbContext.ParkingSystemSettings.AsNoTracking().SingleAsync(cancellationToken);
        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .ToListAsync(cancellationToken);

        foreach (var period in periods)
        {
            var visits = await dbContext.Visits.AsNoTracking()
                .Where(x => x.Status == VisitStatus.Completed &&
                            x.ActualEndAt.HasValue &&
                            x.Id != completedVisit.Id &&
                            x.StartAt < period.ValidUntil &&
                            x.ActualEndAt.Value > period.ValidFrom)
                .ToListAsync(cancellationToken);
            visits.Add(completedVisit);

            var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, visits, ruleSets);
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
