using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed class StartVisitProviderReconciler(IParkingProvider provider, IProviderStartResultStore resultStore)
{
    public async Task<ProviderAction?> ReconcileAsync(ProviderStartPreparation preparation, string licensePlate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Operation.Status != ProviderOperationStatus.Unknown || preparation.Action.Health != ProviderActionHealth.Unknown)
            throw new InvalidOperationException("Only an unknown provider start can be reconciled.");

        var actions = string.IsNullOrWhiteSpace(preparation.Action.ProviderProductId)
            ? await provider.GetActionsAsync(cancellationToken)
            : await provider.GetActionsForProductAsync(preparation.Action.ProviderProductId, cancellationToken);

        var match = ProviderActionMatchPolicy.FindUniqueMatch(
            actions,
            new ProviderActionMatchCriteria(
                preparation.Action.ProviderActionId,
                preparation.Action.ProviderProductId,
                licensePlate,
                ["active", "scheduled"],
                preparation.Action.PlannedStartAt,
                preparation.Action.PlannedEndAt));

        if (match is null)
        {
            if (string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
            {
                // A matching plate/start with a changed end or status is evidence that
                // the start may have executed. Ambiguous/evidence matches must not trigger a retry.
                var hasExecutionEvidence = actions.Any(action =>
                    (string.IsNullOrWhiteSpace(preparation.Action.ProviderProductId) ||
                     string.Equals(action.ProductId, preparation.Action.ProviderProductId, StringComparison.Ordinal)) &&
                    ProviderActionMatchPolicy.LicensePlatesMatch(action.LicensePlate, licensePlate) &&
                    ProviderActionMatchPolicy.TimestampsMatch(action.Start, preparation.Action.PlannedStartAt));

                if (!hasExecutionEvidence)
                    await resultStore.RecordRetryableAsync(preparation, cancellationToken);
            }

            return null;
        }

        preparation.Operation.BeginReconciliation();
        preparation.Action.BeginReconciliation();
        await resultStore.RecordConfirmedAsync(preparation, match, cancellationToken);
        return match;
    }
}
