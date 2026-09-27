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
    public StartVisitPreparation Prepare(StartVisitCommand command, StartVisitContext context, EffectiveParkingPolicy policy, bool requiresProviderCoverageNow)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        if (context.Actor.Id != command.ActorUserId || context.Owner.Id != command.OwnerUserId || context.Vehicle.Id != command.VehicleId)
            throw new InvalidOperationException("Resolved start context does not match the command.");
        StartVisitAuthorization.Validate(context.Actor, context.Owner, context.Vehicle);
        if (command.OperationId == Guid.Empty) throw new ArgumentException("OperationId is required.", nameof(command));
        if (command.OwnerUserId == Guid.Empty || command.ActorUserId == Guid.Empty || command.VehicleId == Guid.Empty) throw new ArgumentException("Owner, actor and vehicle are required.", nameof(command));

        if (command.DesiredEndAt is not null)
        {
            var durationValidation = VisitDurationPolicyValidator.Validate(policy, command.StartAt, command.DesiredEndAt.Value);
            if (!durationValidation.IsAllowed)
                throw new InvalidOperationException("Requested Visit duration exceeds the effective parking policy.");
        }
        var snapshot = EffectiveParkingPolicySnapshot.Capture(policy);
        var visit = new Visit(Guid.NewGuid(), command.OperationId, command.OwnerUserId, command.VehicleId, command.ActorUserId, command.StartAt, command.DesiredEndAt, snapshot);
        return new StartVisitPreparation(visit, command.OperationId, requiresProviderCoverageNow);
    }
}

public sealed class StartVisitClaimer(IVisitCapacityClaimer capacityClaimer)
{
    public async Task<StartVisitClaimResult?> ClaimAsync(
        StartVisitPreparation preparation,
        int maxConcurrentVisits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var claim = await capacityClaimer.TryClaimAsync(
            preparation.Visit,
            maxConcurrentVisits,
            cancellationToken);

        if (!claim.Claimed || claim.Visit is null)
            return null;

        return new StartVisitClaimResult(
            claim.Visit,
            claim.IsReplay,
            preparation.RequiresProviderCoverageNow);
    }
}
