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

        var actions = await provider.GetActionsAsync(cancellationToken);
        var match = !string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId)
            ? actions.SingleOrDefault(x =>
                x.ProviderActionId == preparation.Action.ProviderActionId &&
                string.Equals(x.LicensePlate, licensePlate, StringComparison.OrdinalIgnoreCase))
            : actions.SingleOrDefault(x =>
                string.Equals(x.LicensePlate, licensePlate, StringComparison.OrdinalIgnoreCase) &&
                x.Start == preparation.Action.PlannedStartAt &&
                x.End == preparation.Action.PlannedEndAt);

        if (match is null)
        {
            if (string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
                await resultStore.RecordRetryableAsync(preparation, cancellationToken);
            return null;
        }

        preparation.Operation.BeginReconciliation();
        preparation.Action.BeginReconciliation();
        await resultStore.RecordConfirmedAsync(preparation, match, cancellationToken);
        return match;
    }
}
