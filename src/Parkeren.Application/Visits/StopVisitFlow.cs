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
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<StopVisitFlowResult> StopAsync(
        StopVisitCommand command,
        StopVisitContext context,
        CancellationToken cancellationToken = default)
    {
        StopVisitPreconditions.Validate(command, context);

        var claim = await claimer.ClaimAsync(command, cancellationToken);

        if (claim.IsAlreadyCompleted)
            return new(claim.Visit, claim.IsReplay, StopVisitFlowOutcome.Completed);

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
