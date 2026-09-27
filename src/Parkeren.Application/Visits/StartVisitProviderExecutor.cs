using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartRequest(string LicensePlate, string Location, DateTimeOffset EndAt);
public sealed record ProviderStartExecution(ProviderStartPreparation Preparation, ProviderAction? ProviderAction, bool RequiresReconciliation);

public sealed class StartVisitProviderExecutor(IParkingProvider provider, IProviderStartResultStore resultStore)
{
    public async Task<ProviderStartExecution> ExecuteAsync(
        ProviderStartPreparation preparation,
        ProviderStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(request);

        if (preparation.Operation.Status is ProviderOperationStatus.Unknown or ProviderOperationStatus.Reconciling)
            return new(preparation, null, true);
        if (preparation.Operation.Status != ProviderOperationStatus.InProgress || preparation.Action.State != ProviderActionState.Starting)
            throw new InvalidOperationException("Provider start is not ready for mutation.");

        try
        {
            var action = await provider.StartActionAsync(
                new ProviderParkingActionRequest(request.LicensePlate, preparation.Action.PlannedStartAt, request.EndAt, request.Location),
                cancellationToken);
            await resultStore.RecordConfirmedAsync(preparation, action, cancellationToken);
            return new(preparation, action, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await resultStore.RecordUnknownAsync(preparation, "timeout", cancellationToken);
            return new(preparation, null, true);
        }
        catch (HttpRequestException)
        {
            await resultStore.RecordUnknownAsync(preparation, "network", cancellationToken);
            return new(preparation, null, true);
        }
    }
}
