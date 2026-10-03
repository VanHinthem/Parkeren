using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public enum StopVisitFlowOutcome
{
    Completed,
    ReconciliationRequired
}

public sealed record StopVisitFlowResult(
    Visit Visit,
    bool IsReplay,
    StopVisitFlowOutcome Outcome);

public sealed class StopVisitFlow(
    IStopVisitClaimer claimer,
    IStopVisitFinalizer finalizer,
    IProviderStopStore providerStopStore,
    StopVisitProviderExecutor providerExecutor,
    TimeProvider? timeProvider = null,
    IProviderExtendStopCoordinator? extendStopCoordinator = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<StopVisitFlowResult> StopAsync(
        StopVisitCommand command,
        StopVisitContext context,
        CancellationToken cancellationToken = default)
    {
        StopVisitPreconditions.Validate(command, context);

        var claim = await claimer.ClaimAsync(command, cancellationToken);
        return await ContinueAsync(claim, cancellationToken);
    }

    public Task<StopVisitFlowResult> ResumePersistedStopAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Visit.Status != VisitStatus.Stopping ||
            claim.Operation?.Type != ProviderOperationType.Stop ||
            claim.IsAlreadyCompleted)
            throw new InvalidOperationException("Only a persisted Stop for a Stopping Visit can be resumed.");

        return ContinueAsync(claim, cancellationToken);
    }

    private async Task<StopVisitFlowResult> ContinueAsync(
        StopVisitClaim claim,
        CancellationToken cancellationToken)
    {
        if (claim.IsAlreadyCompleted)
            return new(claim.Visit, claim.IsReplay, StopVisitFlowOutcome.Completed);

        if (extendStopCoordinator is not null &&
            !await extendStopCoordinator.WaitForInFlightExtensionsAsync(claim.Visit.Id, cancellationToken))
            return new(claim.Visit, claim.IsReplay, StopVisitFlowOutcome.ReconciliationRequired);

        while (await finalizer.RequiresProviderActionAsync(claim, cancellationToken))
        {
            var preparation = await providerStopStore.PrepareAttemptAsync(claim, cancellationToken);
            var execution = await providerExecutor.ExecuteAsync(preparation, cancellationToken);
            if (execution.RequiresReconciliation)
                return new(claim.Visit, claim.IsReplay, StopVisitFlowOutcome.ReconciliationRequired);
        }

        var completed = await finalizer.CompleteWithoutProviderActionAsync(
            claim,
            clock.GetUtcNow(),
            cancellationToken);
        return new(completed, claim.IsReplay, StopVisitFlowOutcome.Completed);
    }
}
