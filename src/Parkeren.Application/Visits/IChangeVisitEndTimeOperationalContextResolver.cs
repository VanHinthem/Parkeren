using Parkeren.Domain.Rules;

namespace Parkeren.Application.Visits;

public sealed record ChangeVisitEndTimeOperationalContext(
    IReadOnlyCollection<ParkingRuleSet> RuleSets);

public interface IChangeVisitEndTimeOperationalContextResolver
{
    Task<ChangeVisitEndTimeOperationalContext?> ResolveAsync(
        DateTimeOffset visitStartedAt,
        DateTimeOffset requestedEndAt,
        CancellationToken cancellationToken = default);
}
