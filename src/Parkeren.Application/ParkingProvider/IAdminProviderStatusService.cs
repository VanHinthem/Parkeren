namespace Parkeren.Application.ParkingProvider;

public sealed record AdminProviderStatus(
    ProviderProduct? Product,
    ProviderBalance? Balance,
    bool BalanceIsStale,
    DateTimeOffset? LastSuccessfulBalanceAt,
    DateTimeOffset LastBalanceAttemptAt,
    string? BalanceError,
    IReadOnlyList<ProviderParkingAction> Actions,
    DateTimeOffset? ActionsRetrievedAt,
    string? ActionsError);

public interface IAdminProviderStatusService
{
    Task<AdminProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
