using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StartVisitFlowResult(Visit Visit, bool IsReplay, bool RequiresProviderCoverageNow);

public sealed class StartVisitFlow(
    StartVisitPreparer preparer,
    StartVisitClaimer claimer,
    StartVisitFinalizer finalizer)
{
    public async Task<StartVisitFlowResult?> StartAsync(
        StartVisitCommand command,
        StartVisitContext context,
        EffectiveParkingPolicy policy,
        IEnumerable<ParkingRuleSet> ruleSets,
        DateTimeOffset coverageEvaluationEndAt,
        int maxConcurrentVisits,
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

        if (!claim.RequiresProviderCoverageNow)
            await finalizer.FinalizeFreeStartAsync(claim, cancellationToken);

        return new StartVisitFlowResult(
            claim.Visit,
            claim.IsReplay,
            claim.RequiresProviderCoverageNow);
    }
}
