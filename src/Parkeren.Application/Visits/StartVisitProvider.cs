using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record ProviderStartPreparation(
    ProviderOperation Operation,
    ProviderParkingAction Action,
    bool IsReplay,
    bool AttemptStartedNow = false,
    IDisposable? ExecutionLease = null);

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

        var action = new ProviderParkingAction(
            Guid.NewGuid(),
            claim.Visit.Id,
            claim.Visit.StartAt,
            providerEndAt,
            claim.Visit.ProviderProductExternalId,
            claim.Visit.ProviderLocation);
        var operation = new ProviderOperation(Guid.NewGuid(), claim.Visit.StartOperationId, claim.Visit.Id, action.Id, ProviderOperationType.Start);
        return new ProviderStartPreparation(operation, action, claim.IsReplay, true);
    }
}


public sealed record ProviderExtendPreparation(
    ProviderOperation Operation,
    ProviderParkingAction Action,
    DateTimeOffset ProviderEndAt,
    bool IsReplay,
    bool AttemptStartedNow = false,
    IDisposable? ExecutionLease = null);

public sealed class ContinueVisitProviderPreparer
{
    public ProviderExtendPreparation Prepare(
        Visit visit,
        ProviderParkingAction action,
        Guid operationId,
        DateTimeOffset providerEndAt)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(action);
        if (visit.Status != VisitStatus.Active)
            throw new InvalidOperationException("Provider continuation can only be prepared for an Active Visit.");
        if (action.VisitId != visit.Id || action.State != ProviderActionState.Active)
            throw new InvalidOperationException("Provider continuation requires the Visit's active provider action.");
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation id is required.", nameof(operationId));
        if (providerEndAt <= action.PlannedEndAt)
            throw new ArgumentOutOfRangeException(nameof(providerEndAt));

        var operation = new ProviderOperation(
            Guid.NewGuid(),
            operationId,
            visit.Id,
            action.Id,
            ProviderOperationType.Extend);

        return new ProviderExtendPreparation(operation, action, providerEndAt, false, false);
    }
}


public interface IProviderExtendStore
{
    Task<ProviderExtendPreparation> PrepareAttemptAsync(
        Visit visit,
        ProviderParkingAction action,
        Guid operationId,
        DateTimeOffset providerEndAt,
        CancellationToken cancellationToken = default);
}
