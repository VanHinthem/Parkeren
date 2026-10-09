using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Counts distinct provider actions explicitly observed as active and external
/// by VisitRecovery. This is a read-only input for future capacity decisions:
/// it does not infer activity from missing end times or history imports.
/// </summary>
public sealed class ExternalActiveProviderActionCounter(ParkerenDbContext db)
{
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        db.ProviderDiscrepancies.AsNoTracking()
            .Where(x => x.Type == ProviderDiscrepancyType.ExternalProviderAction &&
                        x.Status == ProviderDiscrepancyStatus.Open &&
                        x.ProviderActionId != null &&
                        x.ProviderStatus != null &&
                        x.ProviderStatus.ToUpper() == "ACTIVE" &&
                        x.ProviderEndAt == null)
            .Select(x => new { x.ProviderProductId, x.ProviderActionId })
            .Distinct()
            .CountAsync(cancellationToken);
}
