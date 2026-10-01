using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed class ContinueVisitStartExecutor(
    IParkingProvider provider,
    IProviderContinuationStartResultStore resultStore,
    IProviderContinuationStartMutationGuard mutationGuard,
    TimeProvider timeProvider)
{
    public Task<ProviderStartExecution> ExecuteAsync(
        ProviderStartPreparation preparation,
        string licensePlate,
        string location,
        CancellationToken cancellationToken = default)
    {
        if (preparation.Operation.Type != ProviderOperationType.ContinueStart)
            throw new InvalidOperationException("Expected a provider continuation start operation.");

        var reconciler = new StartVisitProviderReconciler(provider, resultStore);
        var executor = new StartVisitProviderExecutor(
            provider, resultStore, reconciler, timeProvider, mutationGuard);
        return executor.ExecuteAsync(
            preparation,
            new ProviderStartRequest(
                licensePlate,
                location,
                preparation.Action.PlannedEndAt,
                preparation.Action.ProviderProductId),
            cancellationToken);
    }
}
