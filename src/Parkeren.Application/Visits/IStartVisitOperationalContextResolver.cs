using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;

namespace Parkeren.Application.Visits;

public sealed record StartVisitOperationalContext(
    EffectiveParkingPolicy Policy,
    IReadOnlyCollection<ParkingRuleSet> RuleSets,
    DateTimeOffset CoverageEvaluationEndAt,
    int MaxConcurrentVisits);

public interface IStartVisitOperationalContextResolver
{
    Task<StartVisitOperationalContext?> ResolveAsync(
        Guid ownerUserId,
        DateTimeOffset startAt,
        DateTimeOffset? desiredEndAt,
        CancellationToken cancellationToken = default);
}
