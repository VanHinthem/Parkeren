using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartRequest(string LicensePlate, string Location, DateTimeOffset EndAt);
public sealed record ProviderStartExecution(ProviderStartPreparation Preparation, ProviderAction? ProviderAction, bool RequiresReconciliation, bool DefinitiveFailure = false);

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
        if (preparation.IsReplay &&
            preparation.Operation.Status == ProviderOperationStatus.InProgress &&
            preparation.Action.State == ProviderActionState.Starting)
        {
            await resultStore.RecordUnknownAsync(preparation, "interrupted-in-progress", cancellationToken);
            return new(preparation, null, true);
        }
        if (preparation.Operation.Status == ProviderOperationStatus.Succeeded &&
            preparation.Action.State == ProviderActionState.Active &&
            !string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
        {
            return new(
                preparation,
                new ProviderAction(
                    preparation.Action.ProviderActionId,
                    request.LicensePlate,
                    preparation.Action.ActualStartAt ?? preparation.Action.PlannedStartAt,
                    preparation.Action.ActualEndAt ?? preparation.Action.PlannedEndAt,
                    request.Location,
                    preparation.Action.ProviderStatus ?? "active"),
                false);
        }
        if (preparation.Operation.Status != ProviderOperationStatus.InProgress || preparation.Action.State != ProviderActionState.Starting)
            throw new InvalidOperationException("Provider start is not ready for mutation.");

        try
        {
            if (request.EndAt != preparation.Action.PlannedEndAt)
                throw new InvalidOperationException("Provider request end must match the persisted planned end.");

            var action = await provider.StartActionAsync(
                new ProviderParkingActionRequest(request.LicensePlate, preparation.Action.PlannedStartAt, preparation.Action.PlannedEndAt, request.Location),
                cancellationToken);

            var actions = await provider.GetActionsAsync(cancellationToken);
            var confirmed = actions.SingleOrDefault(x =>
                x.ProviderActionId == action.ProviderActionId &&
                string.Equals(x.LicensePlate, request.LicensePlate, StringComparison.OrdinalIgnoreCase) &&
                x.Start == preparation.Action.PlannedStartAt &&
                x.End == preparation.Action.PlannedEndAt);

            if (confirmed is null)
            {
                await resultStore.RecordUnknownAsync(preparation, "read-back-unconfirmed", cancellationToken);
                return new(preparation, action, true);
            }

            await resultStore.RecordConfirmedAsync(preparation, confirmed, cancellationToken);
            return new(preparation, confirmed, false);
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
        catch (System.Text.Json.JsonException)
        {
            await resultStore.RecordUnknownAsync(preparation, "invalid-response", cancellationToken);
            return new(preparation, null, true);
        }
        catch (ArgumentException)
        {
            await resultStore.RecordDefinitiveFailureAsync(preparation, "provider-rejected", cancellationToken);
            return new(preparation, null, false, true);
        }
    }
}
