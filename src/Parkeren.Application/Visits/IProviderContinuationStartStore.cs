using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public interface IProviderContinuationStartStore
{
    Task<ProviderStartPreparation> PrepareInitialCoverageAsync(
        Visit visit,
        Guid operationId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default);

    Task<ProviderStartPreparation> PrepareAttemptAsync(
        Visit visit,
        ProviderParkingAction precedingAction,
        Guid operationId,
        DateTimeOffset newEndAt,
        CancellationToken cancellationToken = default);
}

public interface IProviderContinuationStartResultStore : IProviderStartResultStore
{
}

public interface IProviderContinuationStartMutationGuard : IProviderStartMutationGuard
{
}
