namespace Parkeren.Application.Visits;

public enum ChangeVisitEndTimeFlowOutcome
{
    Changed,
    Stopped,
    ReconciliationRequired
}

public sealed record ChangeVisitEndTimeFlowResult(
    Parkeren.Domain.Visits.Visit Visit,
    bool IsReplay,
    ChangeVisitEndTimeFlowOutcome Outcome);

public sealed class ChangeVisitEndTimeFlow(
    IVisitEndTimeChanger changer,
    StopVisitFlow stopFlow,
    IVisitEndTimeProviderAdjuster? providerAdjuster = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ChangeVisitEndTimeFlowResult> ChangeAsync(
        ChangeVisitEndTimeCommand command,
        StopVisitContext context,
        CancellationToken cancellationToken = default)
    {
        StopVisitPreconditions.Validate(
            new StopVisitCommand(command.OperationId, command.VisitId, command.ActorUserId),
            context);

        if (ChangeVisitEndTimeRouting.Resolve(command.DesiredEndAt, clock.GetUtcNow()) == ChangeVisitEndTimeRoute.Stop)
        {
            var stop = await stopFlow.StopAsync(
                new StopVisitCommand(command.OperationId, command.VisitId, command.ActorUserId),
                context,
                cancellationToken);

            return new(
                stop.Visit,
                stop.IsReplay,
                stop.Outcome == StopVisitFlowOutcome.ReconciliationRequired
                    ? ChangeVisitEndTimeFlowOutcome.ReconciliationRequired
                    : ChangeVisitEndTimeFlowOutcome.Stopped);
        }

        if (command.DesiredEndAt is not null && command.DesiredEndAt < context.Visit.DesiredEndAt)
        {
            if (providerAdjuster is not null)
            {
                var adjustment = await providerAdjuster.AdjustAsync(command, cancellationToken);
                if (adjustment.RequiresReconciliation)
                    return new(context.Visit, false, ChangeVisitEndTimeFlowOutcome.ReconciliationRequired);
            }
        }

        var change = await changer.ApplyAsync(command, cancellationToken);
        return new(change.Visit, change.IsReplay, ChangeVisitEndTimeFlowOutcome.Changed);
    }
}
