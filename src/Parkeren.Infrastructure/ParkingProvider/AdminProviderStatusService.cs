using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

internal sealed class AdminProviderStatusCache
{
    private readonly object gate = new();
    private ProviderProduct? product;
    private ProviderBalance? balance;

    public void StoreBalance(ProviderProduct currentProduct, ProviderBalance currentBalance)
    {
        lock (gate)
        {
            product = currentProduct;
            balance = currentBalance;
        }
    }

    public (ProviderProduct? Product, ProviderBalance? Balance) ReadBalance()
    {
        lock (gate)
            return (product, balance);
    }
}

internal sealed class AdminProviderStatusService(
    IParkingProvider provider,
    AdminProviderStatusCache cache) : IAdminProviderStatusService
{
    public async Task<AdminProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var balanceAttemptAt = DateTimeOffset.UtcNow;
        ProviderProduct? product = null;
        ProviderBalance? balance = null;
        string? balanceError = null;
        var balanceIsStale = false;
        DateTimeOffset? lastSuccessfulBalanceAt = null;

        try
        {
            product = await provider.GetProductAsync(cancellationToken);
            balance = await provider.GetBalanceAsync(cancellationToken);
            cache.StoreBalance(product, balance);
            lastSuccessfulBalanceAt = balance.RetrievedAt;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            balanceError = exception.Message;
            var cached = cache.ReadBalance();
            product = cached.Product;
            balance = cached.Balance;
            balanceIsStale = balance is not null;
            lastSuccessfulBalanceAt = balance?.RetrievedAt;
        }

        IReadOnlyList<ProviderParkingAction> actions = Array.Empty<ProviderParkingAction>();
        DateTimeOffset? actionsRetrievedAt = null;
        string? actionsError = null;

        try
        {
            actions = await provider.GetActionsAsync(cancellationToken);
            actionsRetrievedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            actionsError = exception.Message;
        }

        return new AdminProviderStatus(
            product,
            balance,
            balanceIsStale,
            lastSuccessfulBalanceAt,
            balanceAttemptAt,
            balanceError,
            actions,
            actionsRetrievedAt,
            actionsError);
    }
}
