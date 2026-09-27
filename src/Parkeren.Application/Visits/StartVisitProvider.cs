using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartPreparation(ProviderOperation Operation, ProviderParkingAction Action, bool IsReplay);

public sealed class StartVisitProviderPreparer
{
    public ProviderStartPreparation Prepare(StartVisitClaimResult claim, DateTimeOffset providerEndAt)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.RequiresProviderCoverageNow)
            throw new InvalidOperationException("A free-period Visit does not require a provider start operation.");
        if (claim.Visit.Status != VisitStatus.Starting)
            throw new InvalidOperationException("Provider coverage can only be prepared for a Starting Visit.");
        if (providerEndAt <= claim.Visit.StartAt)
            throw new ArgumentOutOfRangeException(nameof(providerEndAt));

        var action = new ProviderParkingAction(Guid.NewGuid(), claim.Visit.Id, claim.Visit.StartAt, providerEndAt);
        var operation = new ProviderOperation(Guid.NewGuid(), claim.Visit.StartOperationId, claim.Visit.Id, action.Id, ProviderOperationType.Start);
        return new ProviderStartPreparation(operation, action, claim.IsReplay);
    }
}
