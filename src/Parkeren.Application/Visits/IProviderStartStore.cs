using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public interface IProviderStartStore
{
    Task<ProviderStartPreparation> PrepareAttemptAsync(
        StartVisitClaimResult claim,
        DateTimeOffset providerEndAt,
        CancellationToken cancellationToken = default);
}
