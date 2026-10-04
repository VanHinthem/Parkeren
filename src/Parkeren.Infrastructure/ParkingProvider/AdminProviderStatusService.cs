using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

internal sealed class AdminProviderStatusService(
    ParkerenDbContext dbContext,
    IParkingProvider provider,
    IProviderProductCatalogService productCatalog) : IAdminProviderStatusService
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
                await dbContext.ParkingProviderProducts
                    .Where(x => x.Id == defaultProduct.Id &&
                        (!x.LastSuccessfulBalanceAt.HasValue || x.LastSuccessfulBalanceAt.Value < balance.RetrievedAt))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.LastSuccessfulBalance, (decimal?)balance.RemainingBalance)
                        .SetProperty(x => x.LastSuccessfulBalanceUnit, (string?)balance.Unit.ToString())
                        .SetProperty(x => x.LastSuccessfulBalanceAt, (DateTimeOffset?)balance.RetrievedAt),
                        cancellationToken);
                lastSuccessfulBalanceAt = balance.RetrievedAt;
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
            {
                balanceError = exception.Message;
                var persistedProduct = await dbContext.ParkingProviderProducts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == defaultProduct.Id, cancellationToken);
                if (persistedProduct?.LastSuccessfulBalance is decimal lastBalance &&
                    persistedProduct.LastSuccessfulBalanceUnit is string unitName &&
                    persistedProduct.LastSuccessfulBalanceAt is DateTimeOffset retrievedAt &&
                    Enum.TryParse<ProviderBalanceUnit>(unitName, out var unit))
                {
                    balance = new ProviderBalance(lastBalance, unit, retrievedAt);
                    balanceIsStale = true;
                    lastSuccessfulBalanceAt = retrievedAt;
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
