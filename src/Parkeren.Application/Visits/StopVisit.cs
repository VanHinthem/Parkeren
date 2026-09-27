using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StopVisitCommand(Guid OperationId, Guid VisitId, Guid ActorUserId);

public sealed record StopVisitClaim(
    Visit Visit,
    ProviderOperation? Operation,
    bool IsReplay,
    bool IsAlreadyCompleted);

public interface IStopVisitClaimer
{
    Task<StopVisitClaim> ClaimAsync(
        StopVisitCommand command,
        CancellationToken cancellationToken = default);
}

public interface IStopVisitFinalizer
{
    Task<Visit> CompleteWithoutProviderActionAsync(
        StopVisitClaim claim,
        DateTimeOffset actualEndAt,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderStopPreparation(
    ProviderOperation Operation,
    ProviderParkingAction Action,
    bool IsReplay,
    bool AttemptStartedNow);

public interface IProviderStopStore
{
    Task<ProviderStopPreparation> PrepareAttemptAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken = default);
}

public interface IProviderStopResultStore
{
    Task RecordUnknownAsync(
        ProviderStopPreparation preparation,
        string errorCode,
        CancellationToken cancellationToken = default);

    Task RecordConfirmedAsync(
        ProviderStopPreparation preparation,
        Parkeren.Application.ParkingProvider.ProviderParkingAction providerAction,
        DateTimeOffset actualEndAt,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderStopExecution(
    ProviderStopPreparation Preparation,
    Parkeren.Application.ParkingProvider.ProviderParkingAction? ProviderAction,
    bool RequiresReconciliation);

public sealed class StopVisitProviderExecutor(
    Parkeren.Application.ParkingProvider.IParkingProvider provider,
    IProviderStopResultStore resultStore,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan AttemptLease = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ProviderStopExecution> ExecuteAsync(
        ProviderStopPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (preparation.Operation.Status is ProviderOperationStatus.Unknown or ProviderOperationStatus.Reconciling)
            return new(preparation, null, true);

        if (preparation.IsReplay &&
            !preparation.AttemptStartedNow &&
            preparation.Operation.Status == ProviderOperationStatus.InProgress &&
            preparation.Action.State == ProviderActionState.Stopping)
        {
            var attemptStartedAt = preparation.Operation.AttemptStartedAt;
            if (attemptStartedAt is not null && clock.GetUtcNow() - attemptStartedAt.Value < AttemptLease)
                return new(preparation, null, true);

            await resultStore.RecordUnknownAsync(preparation, "stale-in-progress", cancellationToken);
            return new(preparation, null, true);
        }

        if (preparation.Operation.Status == ProviderOperationStatus.Succeeded &&
            preparation.Action.State == ProviderActionState.Stopped)
            return new(preparation, null, false);

        if (!preparation.AttemptStartedNow ||
            preparation.Operation.Status != ProviderOperationStatus.InProgress ||
            preparation.Action.State != ProviderActionState.Stopping ||
            string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
            throw new InvalidOperationException("Provider Stop is not ready for mutation.");

        try
        {
            await provider.StopActionAsync(preparation.Action.ProviderActionId, cancellationToken);

            var actions = await provider.GetActionsAsync(cancellationToken);
            var confirmed = actions.SingleOrDefault(x =>
                x.ProviderActionId == preparation.Action.ProviderActionId &&
                string.Equals(x.Status, "stopped", StringComparison.OrdinalIgnoreCase));

            if (confirmed is null)
            {
                await resultStore.RecordUnknownAsync(preparation, "read-back-unconfirmed", cancellationToken);
                return new(preparation, null, true);
            }

            var actualEndAt = clock.GetUtcNow();
            await resultStore.RecordConfirmedAsync(preparation, confirmed, actualEndAt, cancellationToken);
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
    }
}
