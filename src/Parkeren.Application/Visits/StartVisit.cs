using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record StartVisitCommand(Guid OperationId, Guid OwnerUserId, Guid ActorUserId, Guid VehicleId, DateTimeOffset StartAt, DateTimeOffset? DesiredEndAt);
public sealed record StartVisitPreparation(Visit Visit, Guid OperationId, bool RequiresProviderCoverageNow);

public sealed class StartVisitPreparer
{
    public StartVisitPreparation Prepare(StartVisitCommand command, EffectiveParkingPolicy policy, bool requiresProviderCoverageNow)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (command.OperationId == Guid.Empty) throw new ArgumentException("OperationId is required.", nameof(command));
        if (command.OwnerUserId == Guid.Empty || command.ActorUserId == Guid.Empty || command.VehicleId == Guid.Empty) throw new ArgumentException("Owner, actor and vehicle are required.", nameof(command));

        VisitDurationPolicyValidator.Validate(command.StartAt, command.DesiredEndAt, policy);
        var snapshot = EffectiveParkingPolicySnapshot.Capture(policy);
        var visit = new Visit(Guid.NewGuid(), command.OwnerUserId, command.VehicleId, command.ActorUserId, command.StartAt, command.DesiredEndAt, snapshot);
        return new StartVisitPreparation(visit, command.OperationId, requiresProviderCoverageNow);
    }
}
