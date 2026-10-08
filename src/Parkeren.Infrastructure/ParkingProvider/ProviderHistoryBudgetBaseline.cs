using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Records already-reached budget thresholds after historical import without
/// emitting retrospective notifications. Existing thresholds are never reset.
/// </summary>
internal static class ProviderHistoryBudgetBaseline
{
    public static async Task RecordAsync(
        ParkerenDbContext db, string externalProductId,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var product = await db.ParkingProviderProducts.AsNoTracking()
            .Where(x => x.ProviderProductId == externalProductId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (!product.HasValue)
            return;

        var periods = await db.ParkingBudgetPeriods.AsNoTracking()
            .Where(x => x.ProviderProductId == product.Value).ToListAsync(cancellationToken);
        if (periods.Count == 0)
            return;

        var settings = await db.ParkingSystemSettings.AsNoTracking()
            .SingleAsync(cancellationToken);
        var rules = await db.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows).Include(x => x.CalendarExceptions)
            .Where(x => x.ProviderProductId == product.Value).ToListAsync(cancellationToken);

        foreach (var period in periods)
        {
            var actions = await db.ProviderParkingActions.AsNoTracking()
                .Where(x =>
                    (x.ProviderProductId == externalProductId ||
                     (x.VisitId.HasValue && db.Visits.Any(v =>
                         v.Id == x.VisitId.Value && v.ProviderProductId == product.Value))) &&
                    (x.State == ProviderActionState.Completed || x.State == ProviderActionState.Stopped) &&
                    x.ActualStartAt.HasValue && x.ActualEndAt.HasValue &&
                    x.ActualStartAt.Value < period.ValidUntil &&
                    x.ActualEndAt.Value > period.ValidFrom)
                .ToListAsync(cancellationToken);
            if (actions.Count == 0)
                continue;

            // Missing historical rules must not turn real paid parking into
            // a silently calculated zero-hour budget.
            if (!rules.Any(x => x.ValidFrom < period.ValidUntil &&
                                (x.ValidUntil == null || x.ValidUntil > period.ValidFrom)))
                throw new InvalidOperationException(
                    $"Missing historical parking rules for budget period {period.Id}.");

            var usage = RealizedParkingBudgetUsageCalculator.CalculateFromActions(period, actions, rules);
            var alreadyRecorded = await db.ParkingBudgetWarningStates
                .Where(x => x.ParkingBudgetPeriodId == period.Id)
                .Select(x => x.ThresholdPercentage).ToListAsync(cancellationToken);
            var reached = ParkingBudgetWarningEvaluator.GetHistoricalBaselineThresholds(
                usage, settings.BudgetWarningThresholdPercentages, alreadyRecorded);

            foreach (var threshold in reached)
                db.ParkingBudgetWarningStates.Add(new ParkingBudgetWarningState(
                    Guid.NewGuid(), period.Id, threshold, observedAt));
        }

        // No NotificationEvent, inbox entry or push is generated.
        await db.SaveChangesAsync(cancellationToken);
    }
}
