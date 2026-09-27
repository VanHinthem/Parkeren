using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Visits;
using ProviderAction = Parkeren.Application.ParkingProvider.ProviderParkingAction;

namespace Parkeren.Application.Visits;

public interface IProviderStartResultStore
{
    Task RecordResponseAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default);
    Task RecordConfirmedAsync(ProviderStartPreparation preparation, ProviderAction providerAction, CancellationToken cancellationToken = default);
    Task RecordDefinitiveFailureAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default);
    Task RecordUnknownAsync(ProviderStartPreparation preparation, string? errorCode = null, CancellationToken cancellationToken = default);
}
