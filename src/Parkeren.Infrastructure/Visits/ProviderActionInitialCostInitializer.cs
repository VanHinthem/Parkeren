using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal static class ProviderActionInitialCostInitializer
{
    public static async Task TryInitializeAsync(
        ParkerenDbContext dbContext,
        ProviderParkingAction action,
        CancellationToken cancellationToken)
    {
        if (action.State is not (ProviderActionState.Stopped or ProviderActionState.Completed))
            return;

        if (action.ActualEndAt is not DateTimeOffset actualEndAt)
            return;

        if (action.ActualStartAt is not DateTimeOffset actualStartAt)
        {
            if (action.State == ProviderActionState.Stopped && actualEndAt <= action.PlannedStartAt)
                action.SetInitialProviderCost(0m);
            return;
        }

        if (actualEndAt < actualStartAt)
            return;

        var visit = await dbContext.Visits.AsNoTracking()
            .SingleAsync(x => x.Id == action.VisitId, cancellationToken);
        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ProviderProductId == visit.ProviderProductId &&
                        x.ValidFrom < actualEndAt &&
                        (x.ValidUntil == null || x.ValidUntil > actualStartAt))
            .ToListAsync(cancellationToken);
        var tariffs = await dbContext.ParkingTariffs.AsNoTracking()
            .Where(x => x.ProviderProductId == visit.ProviderProductId &&
                        x.ValidFrom < actualEndAt &&
                        (x.ValidUntil == null || x.ValidUntil > actualStartAt))
            .ToListAsync(cancellationToken);

        try
        {
            var paidSegments = ProviderActionPaidTimeCalculator.CalculatePaidSegments(
                [action], actualStartAt, actualEndAt, ruleSets);
            action.SetInitialProviderCost(ProviderActionCostCalculator.Calculate(paidSegments, tariffs));
        }
        catch (InvalidOperationException)
        {
            // Missing historical rules or tariffs must not block a confirmed provider transition.
        }
    }
}