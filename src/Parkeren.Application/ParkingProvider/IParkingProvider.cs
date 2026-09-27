namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderCategory(string Id, string Name);
public sealed record ProviderProduct(string Id, string Name, string Location);

public sealed record ProviderBalance(TimeSpan RemainingPaidDuration, DateTimeOffset RetrievedAt);

public sealed record ProviderParkingActionRequest(
    string LicensePlate,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location);

public sealed record ProviderParkingAction(
    string ProviderActionId,
    string LicensePlate,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    string Status);

public interface IParkingProvider
{
    Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default);

    Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderParkingAction> StartActionAsync(
        ProviderParkingActionRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderParkingAction> ExtendActionAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default);

    Task StopActionAsync(
        string providerActionId,
        CancellationToken cancellationToken = default);
}
