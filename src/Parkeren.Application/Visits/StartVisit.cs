using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StartVisitCommand(Guid OperationId, Guid OwnerUserId, Guid ActorUserId, Guid VehicleId, DateTimeOffset StartAt, DateTimeOffset? DesiredEndAt);
public sealed record StartVisitPreparation(Visit Visit, Guid OperationId, bool RequiresProviderCoverageNow);
public sealed record StartVisitClaimResult(Visit Visit, bool IsReplay, bool RequiresProviderCoverageNow);

public sealed record StartVisitContext(StartVisitActor Actor, StartVisitOwner Owner, StartVisitVehicle Vehicle);

public sealed class StartVisitPreparer
{
    public StartVisitPreparation Prepare(StartVisitCommand command, StartVisitContext context, EffectiveParkingPolicy policy, IEnumerable<ParkingRuleSet> ruleSets, DateTimeOffset coverageEvaluationEndAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ruleSets);
        var rules = ruleSets.ToArray();
        if (context.Actor.Id != command.ActorUserId || context.Owner.Id != command.OwnerUserId || context.Vehicle.Id != command.VehicleId)
            throw new InvalidOperationException("Resolved start context does not match the command.");
        StartVisitAuthorization.Validate(context.Actor, context.Owner, context.Vehicle);
        if (command.OperationId == Guid.Empty) throw new ArgumentException("OperationId is required.", nameof(command));
        if (command.OwnerUserId == Guid.Empty || command.ActorUserId == Guid.Empty || command.VehicleId == Guid.Empty) throw new ArgumentException("Owner, actor and vehicle are required.", nameof(command));

        if (command.DesiredEndAt is null && !policy.AllowOpenEndedVisits)
            throw new InvalidOperationException("Visit policy does not allow open-ended Visits.");

        if (command.DesiredEndAt is not null)
        {
            var durationValidation = VisitDurationPolicyValidator.Validate(policy, command.StartAt, command.DesiredEndAt.Value);
            if (!durationValidation.IsAllowed)
                throw new InvalidOperationException("Requested Visit duration exceeds the effective parking policy.");

            var paidDuration = ParkingRuleSetPeriodSegmenter.Segment(command.StartAt, command.DesiredEndAt.Value, rules)
                .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
                .Where(x => x.IsPaid)
                .Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));
            if (policy.MaxPaidParkingDuration is TimeSpan maxPaidParkingDuration && paidDuration > maxPaidParkingDuration)
                throw new InvalidOperationException("Requested Visit paid duration exceeds the effective parking policy.");
        }
        var requiresProviderCoverageNow = StartVisitCoverage.RequiresProviderCoverageNow(
            command.StartAt, coverageEvaluationEndAt, rules);
        var snapshot = EffectiveParkingPolicySnapshot.Capture(policy);
        var visit = new Visit(Guid.NewGuid(), command.OperationId, command.OwnerUserId, command.VehicleId, command.ActorUserId, command.StartAt, command.DesiredEndAt, snapshot);
        return new StartVisitPreparation(visit, command.OperationId, requiresProviderCoverageNow);
    }
}

public static class ProviderActionStartPlanner
{
    public static DateTimeOffset PlanEnd(
        DateTimeOffset startAt, DateTimeOffset desiredEndAt, IEnumerable<ParkingRuleSet> ruleSets)
    {
        if (desiredEndAt <= startAt)
            throw new ArgumentOutOfRangeException(nameof(desiredEndAt));

        var activeRules = ruleSets
            .Where(x => x.ValidFrom <= startAt && (x.ValidUntil is null || x.ValidUntil > startAt))
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No parking rules cover the provider action start.");

        var maxEndAt = startAt + activeRules.MaxProviderActionDuration;
        var firstPaid = ParkingRuleSetPeriodSegmenter.Segment(startAt, desiredEndAt, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .FirstOrDefault();
        if (firstPaid is null || firstPaid.Start != startAt || !firstPaid.IsPaid)
            throw new InvalidOperationException("Provider coverage must begin inside a paid parking segment.");
        return new[] { desiredEndAt, maxEndAt, firstPaid.End }.Min();
    }
}

public sealed class StartVisitClaimer(IVisitCapacityClaimer capacityClaimer)
{
    public async Task<StartVisitClaimResult?> ClaimAsync(
        StartVisitPreparation preparation,
        int maxGlobalConcurrentVisits,
        int maxUserConcurrentVisits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var claim = await capacityClaimer.TryClaimAsync(
            preparation.Visit,
            maxGlobalConcurrentVisits,
            maxUserConcurrentVisits,
            cancellationToken);

        if (!claim.Claimed || claim.Visit is null)
            return null;

        return new StartVisitClaimResult(
            claim.Visit,
            claim.IsReplay,
            preparation.RequiresProviderCoverageNow);
    }
}
