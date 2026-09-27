using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public enum StartVisitFlowOutcome
{
    Active,
    ReconciliationRequired,
    DefinitiveFailure
}

public sealed record StartVisitFlowResult(
    Visit Visit,
    bool IsReplay,
    bool RequiresProviderCoverageNow,
    StartVisitFlowOutcome Outcome);
public sealed record StartVisitProviderContext(string LicensePlate, string Location);

public interface IStartVisitNotificationPublisher
{
    Task PublishStartedAsync(Visit visit, CancellationToken cancellationToken = default);
}

public sealed class StartVisitFlow(
    StartVisitPreparer preparer,
    StartVisitClaimer claimer,
    StartVisitFinalizer finalizer,
    StartVisitProviderReadiness providerReadiness,
    IProviderStartStore providerStartStore,
    StartVisitProviderExecutor providerExecutor,
    IStartVisitNotificationPublisher notificationPublisher)
{
    public async Task<StartVisitFlowResult?> StartAsync(
        StartVisitCommand command,
        StartVisitContext context,
        EffectiveParkingPolicy policy,
        IEnumerable<ParkingRuleSet> ruleSets,
        DateTimeOffset coverageEvaluationEndAt,
        int maxConcurrentVisits,
        StartVisitProviderContext? providerContext = null,
        CancellationToken cancellationToken = default)
    {
        var preparation = preparer.Prepare(
            command,
            context,
            policy,
            ruleSets,
            coverageEvaluationEndAt);

        var claim = await claimer.ClaimAsync(
            preparation,
            maxConcurrentVisits,
            cancellationToken);

        if (claim is null)
            return null;

        if (claim.IsReplay && claim.Visit.Status == VisitStatus.Active)
        {
            try
            {
                await notificationPublisher.PublishStartedAsync(claim.Visit, cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Notification delivery is best-effort and must never change Visit start state.
            }

            return new StartVisitFlowResult(
                claim.Visit,
                true,
                claim.RequiresProviderCoverageNow,
                StartVisitFlowOutcome.Active);
        }

        var outcome = StartVisitFlowOutcome.Active;

        if (!claim.RequiresProviderCoverageNow)
        {
            await finalizer.FinalizeFreeStartAsync(claim, cancellationToken);
        }
        else
        {
            if (providerContext is null)
                throw new InvalidOperationException("Server-resolved provider context is required for a paid Visit start.");
            if (string.IsNullOrWhiteSpace(providerContext.LicensePlate) || string.IsNullOrWhiteSpace(providerContext.Location))
                throw new InvalidOperationException("Provider context must contain a license plate and location.");

            var paidEndAt = command.DesiredEndAt ?? coverageEvaluationEndAt;
            var paidDuration = ParkingRuleSetPeriodSegmenter.Segment(command.StartAt, paidEndAt, ruleSets)
                .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
                .Where(x => x.IsPaid)
                .Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));

            await providerReadiness.CheckAsync(paidDuration, cancellationToken);
            var providerPreparation = await providerStartStore.PrepareAttemptAsync(claim, paidEndAt, cancellationToken);
            var execution = await providerExecutor.ExecuteAsync(
                providerPreparation,
                new ProviderStartRequest(providerContext.LicensePlate, providerContext.Location, paidEndAt),
                cancellationToken);

            if (execution.RequiresReconciliation)
            {
                outcome = StartVisitFlowOutcome.ReconciliationRequired;
            }
            else if (execution.DefinitiveFailure)
            {
                outcome = StartVisitFlowOutcome.DefinitiveFailure;
            }
            else
            {
                await finalizer.FinalizePaidStartAsync(claim, execution, cancellationToken);
            }
        }

        if (outcome == StartVisitFlowOutcome.Active)
        {
            try
            {
                await notificationPublisher.PublishStartedAsync(claim.Visit, cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Notification delivery is best-effort and must never change Visit start state.
            }
        }

        return new StartVisitFlowResult(
            claim.Visit,
            claim.IsReplay,
            claim.RequiresProviderCoverageNow,
            outcome);
    }
}
