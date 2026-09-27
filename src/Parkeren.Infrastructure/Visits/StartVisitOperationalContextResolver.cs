using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Visits;

internal sealed class StartVisitOperationalContextResolver(
    ParkerenDbContext dbContext,
    IConfiguration configuration) : IStartVisitOperationalContextResolver
{
    public async Task<StartVisitOperationalContext?> ResolveAsync(
        Guid ownerUserId,
        DateTimeOffset startAt,
        DateTimeOffset? desiredEndAt,
        CancellationToken cancellationToken = default)
    {
        var defaults = await dbContext.DefaultParkingPolicies
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (defaults is null)
            return null;

        var userOverride = await dbContext.UserPolicyOverrides
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == ownerUserId, cancellationToken);
        var policy = ParkingPolicyResolver.Resolve(defaults, userOverride);

        var evaluationEndAt = desiredEndAt
            ?? (policy.MaxVisitElapsedDuration is { } maxElapsed
                ? startAt + maxElapsed
                : startAt + policy.MaxPaidParkingDuration);

        var ruleSets = await dbContext.ParkingRuleSets
            .AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < evaluationEndAt && (x.ValidUntil == null || x.ValidUntil > startAt))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);
        if (ruleSets.Count == 0)
            return null;

        var maxConcurrentVisits = configuration.GetValue<int?>("Parking:MaxConcurrentVisits") ?? 5;
        if (maxConcurrentVisits <= 0)
            throw new InvalidOperationException("Parking:MaxConcurrentVisits must be greater than zero.");

        return new StartVisitOperationalContext(
            policy,
            ruleSets,
            evaluationEndAt,
            maxConcurrentVisits);
    }
}
