using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public interface IVisitStartStore
{
    Task SaveAsync(Visit visit, CancellationToken cancellationToken = default);
    Task<bool> CancelUnpreparedStartAsync(Guid visitId, CancellationToken cancellationToken = default);
}

public sealed class StartVisitFinalizer(IVisitStartStore store)
{
    public Task<bool> CancelUnpreparedStartAsync(Guid visitId, CancellationToken cancellationToken = default) =>
        store.CancelUnpreparedStartAsync(visitId, cancellationToken);

    public async Task<Visit> FinalizeFreeStartAsync(StartVisitClaimResult claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.RequiresProviderCoverageNow)
            throw new InvalidOperationException("A Visit requiring provider coverage cannot use the free-period start path.");

        if (claim.Visit.Status == VisitStatus.Starting)
        {
            claim.Visit.Activate();
            await store.SaveAsync(claim.Visit, cancellationToken);
        }

        return claim.Visit;
    }

    public async Task<Visit> FinalizePaidStartAsync(
        StartVisitClaimResult claim,
        ProviderStartExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(execution);
        if (!claim.RequiresProviderCoverageNow)
            throw new InvalidOperationException("A free-period Visit cannot use the paid start finalizer.");
        if (execution.RequiresReconciliation || execution.DefinitiveFailure || execution.ProviderAction is null)
            throw new InvalidOperationException("Paid Visit cannot be activated without confirmed provider coverage.");

        if (claim.Visit.Status == VisitStatus.Starting)
        {
            claim.Visit.Activate();
            await store.SaveAsync(claim.Visit, cancellationToken);
        }

        return claim.Visit;
    }
}
