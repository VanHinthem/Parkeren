using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StartVisitCommand(Guid OperationId, Guid OwnerUserId, Guid ActorUserId, Guid VehicleId, DateTimeOffset StartAt, DateTimeOffset? DesiredEndAt);
public sealed record StartVisitPreparation(Visit Visit, Guid OperationId, bool RequiresProviderCoverageNow);
public sealed record StartVisitClaimResult(Visit Visit, bool IsReplay, bool RequiresProviderCoverageNow);

public sealed record StartVisitContext(StartVisitActor Actor, StartVisitOwner Owner, StartVisitVehicle Vehicle);

public enum StartVisitPolicyRejectionReason
{
    OpenEndedNotAllowed,
    EndNotAfterStart,
    MaxVisitElapsedDurationExceeded,
    MaxPaidParkingDurationExceeded
}

public sealed record StartVisitPolicyAssessment(
    bool IsAllowed,
    TimeSpan? PaidDuration,
    TimeSpan? ElapsedDuration,
    StartVisitPolicyRejectionReason? RejectionReason);

public static class StartVisitPolicyAssessor
{
    public static StartVisitPolicyAssessment Assess(
        DateTimeOffset startAt,
        DateTimeOffset? desiredEndAt,
        EffectiveParkingPolicy policy,
        IEnumerable<ParkingRuleSet> ruleSets)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ruleSets);

        if (desiredEndAt is null)
        {
            return policy.AllowOpenEndedVisits
                ? new StartVisitPolicyAssessment(true, null, null, null)
                : new StartVisitPolicyAssessment(false, null, null, StartVisitPolicyRejectionReason.OpenEndedNotAllowed);
        }

        var normalizedStart = startAt.ToUniversalTime();
        var normalizedEnd = desiredEndAt.Value.ToUniversalTime();
        var elapsed = normalizedEnd - normalizedStart;
        if (normalizedEnd <= normalizedStart)
            return new StartVisitPolicyAssessment(false, TimeSpan.Zero, elapsed, StartVisitPolicyRejectionReason.EndNotAfterStart);

        var elapsedValidation = VisitDurationPolicyValidator.Validate(policy, normalizedStart, normalizedEnd);
        if (!elapsedValidation.IsAllowed)
            return new StartVisitPolicyAssessment(false, null, elapsed, StartVisitPolicyRejectionReason.MaxVisitElapsedDurationExceeded);

        var paidDuration = ParkingRuleSetPeriodSegmenter.Segment(normalizedStart, normalizedEnd, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .Where(x => x.IsPaid)
            .Aggregate(TimeSpan.Zero, (total, segment) => total + (segment.End - segment.Start));

        var paidValidation = ParkingPolicyValidator.ValidatePaidDuration(policy, TimeSpan.Zero, paidDuration);
        if (!paidValidation.IsAllowed)
            return new StartVisitPolicyAssessment(false, paidDuration, elapsed, StartVisitPolicyRejectionReason.MaxPaidParkingDurationExceeded);

        return new StartVisitPolicyAssessment(true, paidDuration, elapsed, null);
    }
}

public sealed class StartVisitPreparer
{
    public StartVisitPreparation Prepare(
        StartVisitCommand command,
        StartVisitContext context,
        EffectiveParkingPolicy policy,
        IEnumerable<ParkingRuleSet> ruleSets,
        DateTimeOffset coverageEvaluationEndAt,
        Guid? providerProductId = null,
        string? providerProductExternalId = null,
        string? providerLocation = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ruleSets);
        var startAt = command.StartAt.ToUniversalTime();
        var desiredEndAt = command.DesiredEndAt?.ToUniversalTime();
        var evaluationEndAt = coverageEvaluationEndAt.ToUniversalTime();
        var rules = ruleSets.ToArray();
        if (context.Actor.Id != command.ActorUserId || context.Owner.Id != command.OwnerUserId || context.Vehicle.Id != command.VehicleId)
            throw new InvalidOperationException("Resolved start context does not match the command.");
        StartVisitAuthorization.Validate(context.Actor, context.Owner, context.Vehicle);
        if (command.OperationId == Guid.Empty) throw new ArgumentException("OperationId is required.", nameof(command));
        if (command.OwnerUserId == Guid.Empty || command.ActorUserId == Guid.Empty || command.VehicleId == Guid.Empty) throw new ArgumentException("Owner, actor and vehicle are required.", nameof(command));

        var policyAssessment = StartVisitPolicyAssessor.Assess(startAt, desiredEndAt, policy, rules);
        if (!policyAssessment.IsAllowed)
        {
            throw policyAssessment.RejectionReason switch
            {
                StartVisitPolicyRejectionReason.OpenEndedNotAllowed =>
                    new InvalidOperationException("Visit policy does not allow open-ended Visits."),
                StartVisitPolicyRejectionReason.MaxPaidParkingDurationExceeded =>
                    new InvalidOperationException("Requested Visit paid duration exceeds the effective parking policy."),
                _ => new InvalidOperationException("Requested Visit duration exceeds the effective parking policy.")
            };
        }
        var requiresProviderCoverageNow = StartVisitCoverage.RequiresProviderCoverageNow(
            startAt, evaluationEndAt, rules);
        var snapshot = EffectiveParkingPolicySnapshot.Capture(policy);
        var visit = new Visit(
            Guid.NewGuid(),
            command.OperationId,
            command.OwnerUserId,
            command.VehicleId,
            command.ActorUserId,
            startAt,
            desiredEndAt,
            snapshot,
            providerProductId,
            providerProductExternalId,
            providerLocation);
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
