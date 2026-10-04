using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartRequest(
    string LicensePlate,
    string Location,
    DateTimeOffset EndAt,
    string? ProductId = null);
public sealed record ProviderStartExecution(ProviderStartPreparation Preparation, ProviderAction? ProviderAction, bool RequiresReconciliation, bool DefinitiveFailure = false);

public interface IProviderStartMutationGuard
{
    Task<bool> CanStartAsync(Guid visitId, CancellationToken cancellationToken = default);
}

public sealed class StartVisitProviderExecutor(IParkingProvider provider, IProviderStartResultStore resultStore, StartVisitProviderReconciler? reconciler = null, TimeProvider? timeProvider = null, IProviderStartMutationGuard? mutationGuard = null, IProviderOperationExecutionTracker? executionTracker = null)
{
    private static readonly TimeSpan AttemptLease = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public async Task<ProviderStartExecution> ExecuteAsync(
        ProviderStartPreparation preparation,
        ProviderStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(request);

        if (preparation.Operation.Status == ProviderOperationStatus.Unknown)
        {
            if (reconciler is null)
                return new(preparation, null, true);

            var reconciledAction = await reconciler.ReconcileAsync(preparation, request.LicensePlate, cancellationToken);
            if (reconciledAction is null)
                return new(preparation, null, true);

            return new(preparation, reconciledAction, false);
        }
        if (preparation.Operation.Status == ProviderOperationStatus.Reconciling)
            return new(preparation, null, true);
        if (preparation.IsReplay &&
            !preparation.AttemptStartedNow &&
            preparation.Operation.Status == ProviderOperationStatus.InProgress &&
            preparation.Action.State == ProviderActionState.Starting)
        {
            if (executionTracker?.IsActive(preparation.Operation.OperationId) == true)
                return new(preparation, null, true);

            // A replay can race with the request that currently owns this persisted
            // attempt. Only an expired persisted lease may be treated as abandoned.
            var attemptStartedAt = preparation.Operation.AttemptStartedAt;
            if (attemptStartedAt is not null && clock.GetUtcNow() - attemptStartedAt.Value < AttemptLease)
                return new(preparation, null, true);

            await resultStore.RecordUnknownAsync(preparation, "stale-in-progress", cancellationToken);
            if (reconciler is null)
                return new(preparation, null, true);

            var reconciledAction = await reconciler.ReconcileAsync(preparation, request.LicensePlate, cancellationToken);
            return new(preparation, reconciledAction, reconciledAction is null);
        }
        if (preparation.Operation.Status == ProviderOperationStatus.Succeeded &&
            preparation.Action.State is ProviderActionState.Active or ProviderActionState.Scheduled &&
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
                    preparation.Action.ProviderStatus ?? "active",
                    request.ProductId),
                false);
        }
        if (preparation.Operation.Status != ProviderOperationStatus.InProgress || preparation.Action.State != ProviderActionState.Starting)
            throw new InvalidOperationException("Provider start is not ready for mutation.");

        IDisposable? activeAttempt = null;
        try
        {
            activeAttempt = preparation.ExecutionLease;
            if (executionTracker is not null && activeAttempt is null)
                return new(preparation, null, true);

            if (request.EndAt != preparation.Action.PlannedEndAt)
                throw new InvalidOperationException("Provider request end must match the persisted planned end.");

            if (mutationGuard is not null &&
                (!preparation.Operation.VisitId.HasValue ||
                 !await mutationGuard.CanStartAsync(preparation.Operation.VisitId.Value, cancellationToken)))
                return new(preparation, null, true);

            var action = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    request.LicensePlate,
                    preparation.Action.PlannedStartAt,
                    preparation.Action.PlannedEndAt,
                    request.Location,
                    request.ProductId),
                cancellationToken);

            await resultStore.RecordResponseAsync(preparation, action, cancellationToken);

            var actions = string.IsNullOrWhiteSpace(request.ProductId)
                ? await provider.GetActionsAsync(cancellationToken)
                : await provider.GetActionsForProductAsync(request.ProductId, cancellationToken);
            var confirmed = ProviderActionMatchPolicy.FindUniqueMatch(
                actions,
                new ProviderActionMatchCriteria(
                    action.ProviderActionId,
                    request.ProductId,
                    request.LicensePlate,
                    ["active", "scheduled"],
                    preparation.Action.PlannedStartAt,
                    preparation.Action.PlannedEndAt));

            if (confirmed is null)
            {
                await resultStore.RecordUnknownAsync(preparation, "read-back-unconfirmed", cancellationToken);
                return new(preparation, action, true);
            }

            await resultStore.RecordConfirmedAsync(preparation, confirmed, cancellationToken);
            return new(preparation, confirmed, false);
        }
        catch (OperationCanceledException)
        {
            await resultStore.RecordUnknownAsync(preparation, "timeout", CancellationToken.None);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new(preparation, null, true);
        }
        catch (HttpRequestException)
        {
            await resultStore.RecordUnknownAsync(preparation, "network", CancellationToken.None);
            return new(preparation, null, true);
        }
        catch (System.Text.Json.JsonException)
        {
            await resultStore.RecordUnknownAsync(preparation, "invalid-response", CancellationToken.None);
            return new(preparation, null, true);
        }
        catch (ProviderResponseException exception)
        {
            var errorCode = string.IsNullOrWhiteSpace(exception.ProviderCode)
                ? "provider-error"
                : exception.ProviderCode;
            await resultStore.RecordUnknownAsync(preparation, errorCode, CancellationToken.None);
            return new(preparation, null, true);
        }
        catch (ArgumentException)
        {
            await resultStore.RecordDefinitiveFailureAsync(preparation, "provider-rejected", cancellationToken);
            return new(preparation, null, false, true);
        }
        finally
        {
            activeAttempt?.Dispose();
        }
    }
}
