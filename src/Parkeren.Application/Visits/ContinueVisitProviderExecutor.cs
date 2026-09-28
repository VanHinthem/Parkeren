using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed record ProviderExtendExecution(
    ProviderExtendPreparation Preparation,
    ProviderAction? ProviderAction,
    bool RequiresReconciliation,
    bool DefinitiveFailure = false);

public interface IProviderExtendResultStore
{
    Task RecordConfirmedAsync(
        ProviderExtendPreparation preparation,
        ProviderAction providerAction,
        CancellationToken cancellationToken = default);

    Task RecordUnknownAsync(
        ProviderExtendPreparation preparation,
        string errorCode,
        CancellationToken cancellationToken = default);

    Task RecordDefinitiveFailureAsync(
        ProviderExtendPreparation preparation,
        string errorCode,
        CancellationToken cancellationToken = default);
}

public sealed class ContinueVisitProviderExecutor(
    IParkingProvider provider,
    IProviderExtendResultStore resultStore)
{
    public async Task<ProviderExtendExecution> ExecuteAsync(
        ProviderExtendPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (preparation.Operation.Status is ProviderOperationStatus.Unknown or ProviderOperationStatus.Reconciling)
            return new(preparation, null, true);

        if (preparation.IsReplay &&
            !preparation.AttemptStartedNow &&
            preparation.Operation.Status == ProviderOperationStatus.InProgress)
            return new(preparation, null, true);

        if (preparation.Operation.Status != ProviderOperationStatus.InProgress ||
            preparation.Action.State != ProviderActionState.Active ||
            string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
            throw new InvalidOperationException("Provider continuation is not ready for mutation.");

        try
        {
            var action = await provider.ExtendActionAsync(
                preparation.Action.ProviderActionId,
                preparation.ProviderEndAt,
                cancellationToken);

            var actions = await provider.GetActionsAsync(cancellationToken);
            var confirmed = actions.SingleOrDefault(x =>
                x.ProviderActionId == preparation.Action.ProviderActionId &&
                x.End == preparation.ProviderEndAt);

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
