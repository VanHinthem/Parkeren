namespace Parkeren.Application.ParkingProvider;

public sealed record ProviderCategory(string Id, string Name);
public sealed record ProviderProduct(
    string Id,
    string Name,
    string Location,
    string? CategoryId = null,
    string? CategoryName = null);

public enum ProviderBalanceUnit
{
    Unknown,
    Euro,
    Minute,
    Times
}

public sealed record ProviderBalance(decimal RemainingBalance, ProviderBalanceUnit Unit, DateTimeOffset RetrievedAt);

public sealed record ProviderParkingActionRequest(
    string LicensePlate,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    string? ProductId = null);

public sealed record ProviderParkingAction(
    string ProviderActionId,
    string LicensePlate,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    string Status,
    string? ProductId = null);

public interface IParkingProvider
{
    Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<ProviderProduct>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        new[] { await GetProductAsync(cancellationToken) };

    Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default);

    Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default);

    Task<ProviderBalance> GetBalanceForProductAsync(
        string productId,
        CancellationToken cancellationToken = default) =>
        GetBalanceAsync(cancellationToken);

    Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderParkingAction>> GetActionsForProductAsync(
        string productId,
        CancellationToken cancellationToken = default) =>
        GetActionsAsync(cancellationToken);

    Task<ProviderParkingAction> StartActionAsync(
        ProviderParkingActionRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderParkingAction> ExtendActionAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default);

    Task<ProviderParkingAction> ExtendActionForProductAsync(
        string productId,
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default) =>
        ExtendActionAsync(providerActionId, newEnd, cancellationToken);

    Task StopActionAsync(
        string providerActionId,
        CancellationToken cancellationToken = default);

    Task StopActionForProductAsync(
        string productId,
        string providerActionId,
        CancellationToken cancellationToken = default) =>
        StopActionAsync(providerActionId, cancellationToken);
}
