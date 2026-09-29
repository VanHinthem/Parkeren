using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StopVisitCommand(Guid OperationId, Guid VisitId, Guid ActorUserId);
public sealed record StopVisitContext(StopVisitActor Actor, Visit Visit);

public static class StopVisitPreconditions
{
    public static void Validate(StopVisitCommand command, StopVisitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (command.OperationId == Guid.Empty || command.VisitId == Guid.Empty || command.ActorUserId == Guid.Empty)
            throw new ArgumentException("Operation, Visit and actor are required.", nameof(command));
        if (context.Actor.Id != command.ActorUserId || context.Visit.Id != command.VisitId)
            throw new InvalidOperationException("Resolved stop context does not match the command.");

        StopVisitAuthorization.Validate(context.Actor, context.Visit);
    }
}

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
    Task<bool> RequiresProviderActionAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken = default);

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
    StopVisitProviderReconciler? reconciler = null,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan AttemptLease = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ProviderStopExecution> ExecuteAsync(
        ProviderStopPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (preparation.Operation.Status == ProviderOperationStatus.Unknown)
        {
            if (reconciler is null)
                return new(preparation, null, true);

            var reconciledAction = await reconciler.ReconcileAsync(preparation, cancellationToken);
            return new(preparation, reconciledAction, reconciledAction is null);
        }

        if (preparation.Operation.Status == ProviderOperationStatus.Reconciling)
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
            var remaining = actions.SingleOrDefault(x =>
                x.ProviderActionId == preparation.Action.ProviderActionId);
            if (remaining is not null &&
                !string.Equals(remaining.Status, "stopped", StringComparison.OrdinalIgnoreCase))
            {
                await resultStore.RecordUnknownAsync(preparation, "read-back-unconfirmed", cancellationToken);
                return new(preparation, null, true);
            }

            var actualEndAt = clock.GetUtcNow();
            var confirmed = remaining ?? new Parkeren.Application.ParkingProvider.ProviderParkingAction(
                preparation.Action.ProviderActionId!,
                string.Empty,
                preparation.Action.PlannedStartAt,
                actualEndAt,
                string.Empty,
                "stopped");
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

public sealed class StopVisitProviderReconciler(
    Parkeren.Application.ParkingProvider.IParkingProvider provider,
    IProviderStopResultStore resultStore,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<Parkeren.Application.ParkingProvider.ProviderParkingAction?> ReconcileAsync(
        ProviderStopPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Operation.Status != ProviderOperationStatus.Unknown ||
            preparation.Action.State != ProviderActionState.Stopping ||
            preparation.Action.Health != ProviderActionHealth.Unknown ||
            string.IsNullOrWhiteSpace(preparation.Action.ProviderActionId))
            throw new InvalidOperationException("Only an unknown provider Stop with a persisted provider action id can be reconciled.");

        var actions = await provider.GetActionsAsync(cancellationToken);
        var match = actions.SingleOrDefault(x =>
            x.ProviderActionId == preparation.Action.ProviderActionId &&
            string.Equals(x.Status, "stopped", StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return null;

        preparation.Operation.BeginReconciliation();
        preparation.Action.BeginReconciliation();
        await resultStore.RecordConfirmedAsync(preparation, match, clock.GetUtcNow(), cancellationToken);
        return match;
    }
}
