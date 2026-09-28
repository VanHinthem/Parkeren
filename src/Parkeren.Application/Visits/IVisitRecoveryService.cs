using Parkeren.Domain.Visits;

namespace Parkeren.Application.Visits;

public sealed record VisitRecoveryItem(
    Visit Visit,
    IReadOnlyList<ProviderParkingAction> ProviderActions,
    IReadOnlyList<ProviderOperation> UnresolvedOperations);

public interface IVisitRecoveryService
{
    Task<IReadOnlyList<VisitRecoveryItem>> LoadAsync(
        CancellationToken cancellationToken = default);
}
