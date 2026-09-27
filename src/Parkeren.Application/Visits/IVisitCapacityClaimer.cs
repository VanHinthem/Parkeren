using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public interface IVisitCapacityClaimer
{
    Task<bool> TryClaimAsync(Visit visit, int maxConcurrentVisits, CancellationToken cancellationToken = default);
}
