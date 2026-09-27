using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public interface IVisitStartStore
{
    Task SaveAsync(Visit visit, CancellationToken cancellationToken = default);
}

public sealed class StartVisitFinalizer(IVisitStartStore store)
{
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
}
