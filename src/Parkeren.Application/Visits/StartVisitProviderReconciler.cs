using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed class StartVisitProviderReconciler(IParkingProvider provider, IProviderStartResultStore resultStore)
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(1);
    public async Task<ProviderAction?> ReconcileAsync(ProviderStartPreparation preparation, string licensePlate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Operation.Status != ProviderOperationStatus.Unknown || preparation.Action.Health != ProviderActionHealth.Unknown)
            throw new InvalidOperationException("Only an unknown provider start can be reconciled.");

        var actions = await provider.GetActionsAsync(cancellationToken);
        var match = !string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId)
            ? actions.SingleOrDefault(x =>
                x.ProviderActionId == preparation.Action.ProviderActionId &&
                PlatesMatch(x.LicensePlate, licensePlate))
            : actions.SingleOrDefault(x =>
                PlatesMatch(x.LicensePlate, licensePlate) &&
                TimestampsMatch(x.Start, preparation.Action.PlannedStartAt) &&
                TimestampsMatch(x.End, preparation.Action.PlannedEndAt));

        if (match is null)
        {
            if (string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
                await resultStore.RecordRetryableAsync(preparation, cancellationToken);
            return null;
        }

        // A known stopped action proves the start was executed, so it must not
        // be confirmed as active or treated as absent and retried.
        if (!string.Equals(match.Status, "active", StringComparison.OrdinalIgnoreCase))
            return null;

        preparation.Operation.BeginReconciliation();
        preparation.Action.BeginReconciliation();
        await resultStore.RecordConfirmedAsync(preparation, match, cancellationToken);
        return match;
    }

    private static bool TimestampsMatch(DateTimeOffset left, DateTimeOffset right) =>
        (left - right).Duration() < TimestampTolerance;

    private static bool PlatesMatch(string providerPlate, string requestedPlate) =>
        string.Equals(Normalize(providerPlate), Normalize(requestedPlate), StringComparison.Ordinal);

    private static string Normalize(string plate) =>
        string.Concat(plate.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
