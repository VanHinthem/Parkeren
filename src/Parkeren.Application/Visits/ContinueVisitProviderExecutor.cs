using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed record ProviderExtendExecution(
    ProviderExtendPreparation Preparation,
    ProviderAction? ProviderAction,
    bool RequiresReconciliation,
    bool DefinitiveFailure = false);

public interface IProviderExtendMutationGuard
{
    Task<bool> CanExtendAsync(
        Guid visitId,
        Guid providerParkingActionId,
        CancellationToken cancellationToken = default);
}

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
    IProviderExtendResultStore resultStore,
    IProviderExtendMutationGuard? mutationGuard = null)
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
        {
            var attemptStartedAt = preparation.Operation.AttemptStartedAt;
            if (!attemptStartedAt.HasValue ||
                DateTimeOffset.UtcNow - attemptStartedAt.Value < TimeSpan.FromMinutes(5))
                return new(preparation, null, true);

            await resultStore.RecordUnknownAsync(preparation, "stale-in-progress", cancellationToken);
            return new(preparation, null, true);
        }

        if (preparation.Operation.Status != ProviderOperationStatus.InProgress ||
            preparation.Action.State != ProviderActionState.Active ||
            string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
            throw new InvalidOperationException("Provider continuation is not ready for mutation.");

        try
        {
            if (!preparation.Operation.VisitId.HasValue ||
                (mutationGuard is not null &&
                 !await mutationGuard.CanExtendAsync(
                     preparation.Operation.VisitId.Value,
                     preparation.Action.Id,
                     cancellationToken)))
                return new(preparation, null, true);

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


public sealed class ContinueVisitProviderReconciler(
    IParkingProvider provider,
    IProviderExtendResultStore resultStore)
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(1);

    public async Task<ProviderAction?> ReconcileAsync(
        ProviderExtendPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (preparation.Operation.Status != ProviderOperationStatus.Unknown)
            throw new InvalidOperationException("Only an unknown provider continuation can be reconciled.");
        if (string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
            throw new InvalidOperationException("Provider continuation requires a provider action id.");

        var requestedEndAt = preparation.Operation.RequestedEndAt
            ?? throw new InvalidOperationException("Provider continuation requires a persisted requested end.");

        var actions = await provider.GetActionsAsync(cancellationToken);
        var match = actions.SingleOrDefault(x =>
            x.ProviderActionId == preparation.Action.ProviderActionId &&
            TimestampsMatch(x.End, requestedEndAt));

        if (match is null)
            return null;

        preparation.Operation.BeginReconciliation();
        await resultStore.RecordConfirmedAsync(preparation, match, cancellationToken);
        return match;
    }

    private static bool TimestampsMatch(DateTimeOffset left, DateTimeOffset right) =>
        (left - right).Duration() < TimestampTolerance;
}
