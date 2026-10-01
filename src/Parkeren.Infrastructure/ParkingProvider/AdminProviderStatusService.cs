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
    IProviderProductCatalogService productCatalog,
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

        var defaultProduct = await productCatalog.GetDefaultAsync(cancellationToken);
        if (defaultProduct is null)
        {
            balanceError = "Er is geen default parkeerproduct geconfigureerd.";
        }
        else if (!defaultProduct.IsAvailable)
        {
            product = ToProviderProduct(defaultProduct);
            balanceError = "Het default parkeerproduct is niet meer beschikbaar bij de provider.";
        }
        else
        {
            product = ToProviderProduct(defaultProduct);
            try
            {
                balance = await provider.GetBalanceForProductAsync(
                    defaultProduct.ProviderProductId,
                    cancellationToken);
                cache.StoreBalance(product, balance);
                lastSuccessfulBalanceAt = balance.RetrievedAt;
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
            {
                balanceError = exception.Message;
                var cached = cache.ReadBalance();
                if (cached.Product?.Id == product.Id)
                {
                    balance = cached.Balance;
                    balanceIsStale = balance is not null;
                    lastSuccessfulBalanceAt = balance?.RetrievedAt;
                }
            }
        }

        IReadOnlyList<ProviderParkingAction> actions = Array.Empty<ProviderParkingAction>();
        DateTimeOffset? actionsRetrievedAt = null;
        string? actionsError = null;

        if (defaultProduct is null)
        {
            actionsError = "Er is geen default parkeerproduct geconfigureerd.";
        }
        else if (!defaultProduct.IsAvailable)
        {
            actionsError = "Het default parkeerproduct is niet meer beschikbaar bij de provider.";
        }
        else
        {
            try
            {
                actions = await provider.GetActionsForProductAsync(
                    defaultProduct.ProviderProductId,
                    cancellationToken);
                actionsRetrievedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
            {
                actionsError = exception.Message;
            }
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

    private static ProviderProduct ToProviderProduct(ProviderProductSummary product) =>
        new(
            product.ProviderProductId,
            product.Name,
            product.Location,
            product.CategoryId,
            product.CategoryName);
}
