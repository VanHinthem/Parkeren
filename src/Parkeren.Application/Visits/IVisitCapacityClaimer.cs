using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record VisitCapacityClaim(bool Claimed, Visit? Visit, bool IsReplay);

public interface IVisitCapacityClaimer
{
    Task<VisitCapacityClaim> TryClaimAsync(Visit visit, int maxGlobalConcurrentVisits, int maxUserConcurrentVisits, CancellationToken cancellationToken = default);
}
