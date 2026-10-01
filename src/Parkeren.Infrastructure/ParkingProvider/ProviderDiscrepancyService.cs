using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

internal sealed class ProviderDiscrepancyService(
    ParkerenDbContext dbContext) : IProviderDiscrepancyService
{
    public async Task<ProviderDiscrepancySummary> ObserveAsync(
        ProviderDiscrepancyObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var discrepancy = await dbContext.ProviderDiscrepancies
            .SingleOrDefaultAsync(
                x => x.Key == observation.Key &&
                     x.Status == ProviderDiscrepancyStatus.Open,
                cancellationToken);

        if (discrepancy is null &&
            observation.Type == ProviderDiscrepancyType.ExternalProviderAction)
        {
            var resolvedId = await dbContext.ProviderDiscrepancies
                .AsNoTracking()
                .Where(x => x.Key == observation.Key &&
                            x.Type == ProviderDiscrepancyType.ExternalProviderAction &&
                            x.Status == ProviderDiscrepancyStatus.Resolved)
                .OrderByDescending(x => x.ResolvedAt)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (resolvedId != Guid.Empty)
                return await GetSummaryAsync(resolvedId, cancellationToken);
        }

        if (discrepancy is null)
        {
            discrepancy = new ProviderDiscrepancy(
                Guid.NewGuid(),
                observation.Key,
                observation.Type,
                observation.ProviderProductId,
                observation.ObservedAt,
                observation.VisitId,
                observation.ProviderParkingActionId,
                observation.ProviderActionId,
                observation.ProviderStatus,
                observation.ProviderStartAt,
                observation.ProviderEndAt);
            dbContext.ProviderDiscrepancies.Add(discrepancy);
        }
        else
        {
            EnsureSameIdentity(discrepancy, observation);
            discrepancy.Observe(
                observation.ObservedAt,
                observation.ProviderActionId,
                observation.ProviderStatus,
                observation.ProviderStartAt,
                observation.ProviderEndAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetSummaryAsync(discrepancy.Id, cancellationToken);
    }

    public async Task<bool> ResolveAsync(
        string key,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Discrepancy key is required.", nameof(key));

        var discrepancy = await dbContext.ProviderDiscrepancies
            .SingleOrDefaultAsync(
                x => x.Key == key.Trim() &&
                     x.Status == ProviderDiscrepancyStatus.Open,
                cancellationToken);
        if (discrepancy is null)
            return false;

        discrepancy.Resolve(resolvedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ProviderDiscrepancySummary>> GetAsync(
        bool includeResolved,
        CancellationToken cancellationToken = default)
    {
        var query =
            from discrepancy in dbContext.ProviderDiscrepancies.AsNoTracking()
            join product in dbContext.ParkingProviderProducts.AsNoTracking()
                on discrepancy.ProviderProductId equals product.Id
            join action in dbContext.ProviderParkingActions.AsNoTracking()
                on discrepancy.ProviderParkingActionId equals action.Id into actions
            from action in actions.DefaultIfEmpty()
            where includeResolved || discrepancy.Status == ProviderDiscrepancyStatus.Open
            orderby discrepancy.Status, discrepancy.DetectedAt descending
            select new ProviderDiscrepancySummary(
                discrepancy.Id,
                discrepancy.Key,
                discrepancy.Type,
                discrepancy.Status,
                discrepancy.ProviderProductId,
                product.Name,
                product.ProviderProductId,
                discrepancy.VisitId,
                discrepancy.ProviderParkingActionId,
                action == null ? null : action.State,
                action == null ? null : action.PlannedEndAt,
                discrepancy.ProviderActionId,
                discrepancy.ProviderStatus,
                discrepancy.ProviderStartAt,
                discrepancy.ProviderEndAt,
                discrepancy.DetectedAt,
                discrepancy.LastObservedAt,
                discrepancy.ResolvedAt);

        return await query.ToListAsync(cancellationToken);
    }

    private async Task<ProviderDiscrepancySummary> GetSummaryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var summaries = await (
            from discrepancy in dbContext.ProviderDiscrepancies.AsNoTracking()
            join product in dbContext.ParkingProviderProducts.AsNoTracking()
                on discrepancy.ProviderProductId equals product.Id
            join action in dbContext.ProviderParkingActions.AsNoTracking()
                on discrepancy.ProviderParkingActionId equals action.Id into actions
            from action in actions.DefaultIfEmpty()
            where discrepancy.Id == id
            select new ProviderDiscrepancySummary(
                discrepancy.Id,
                discrepancy.Key,
                discrepancy.Type,
                discrepancy.Status,
                discrepancy.ProviderProductId,
                product.Name,
                product.ProviderProductId,
                discrepancy.VisitId,
                discrepancy.ProviderParkingActionId,
                action == null ? null : action.State,
                action == null ? null : action.PlannedEndAt,
                discrepancy.ProviderActionId,
                discrepancy.ProviderStatus,
                discrepancy.ProviderStartAt,
                discrepancy.ProviderEndAt,
                discrepancy.DetectedAt,
                discrepancy.LastObservedAt,
                discrepancy.ResolvedAt))
            .ToListAsync(cancellationToken);

        return summaries.Single();
    }

    private static void EnsureSameIdentity(
        ProviderDiscrepancy discrepancy,
        ProviderDiscrepancyObservation observation)
    {
        if (discrepancy.Type != observation.Type ||
            discrepancy.ProviderProductId != observation.ProviderProductId ||
            discrepancy.VisitId != observation.VisitId ||
            discrepancy.ProviderParkingActionId != observation.ProviderParkingActionId)
            throw new InvalidOperationException(
                $"Open discrepancy key '{observation.Key}' is already used for different context.");
    }
}
