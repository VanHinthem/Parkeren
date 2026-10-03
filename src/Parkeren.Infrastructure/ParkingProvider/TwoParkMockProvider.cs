using System.Net.Http.Json;
using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class TwoParkMockProvider(HttpClient httpClient) : IParkingProvider, IProviderActionHistoryReader
{
    public async Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        (await httpClient.GetFromJsonAsync<MockCategory[]>("api/categories", cancellationToken) ?? [])
            .Select(x => new ProviderCategory(x.Id, x.Name)).ToArray();

    public async Task<IReadOnlyList<ProviderProduct>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        (await httpClient.GetFromJsonAsync<MockProduct[]>("api/products", cancellationToken) ?? [])
            .Select(x => new ProviderProduct(x.Id, x.Name, x.Location, x.CategoryId, x.CategoryName))
            .ToArray();

    public async Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetProductsAsync(cancellationToken);
        return products.Count switch
        {
            1 => products[0],
            0 => throw new InvalidOperationException("Parking provider returned no product."),
            _ => throw new InvalidOperationException("Multiple parking products are available; an explicit product selection is required.")
        };
    }

    public async Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        return await GetBalanceForProductAsync(product.Id, cancellationToken);
    }

    public async Task<ProviderBalance> GetBalanceForProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<MockBalance>(
            "api/balance?productId=" + Uri.EscapeDataString(productId),
            cancellationToken)
            ?? throw new InvalidOperationException("Parking provider returned no balance.");
        return new ProviderBalance((decimal)response.RemainingPaidMinutes, ProviderBalanceUnit.Minute, response.RetrievedAt);
    }

    public async Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        return await GetActionsForProductAsync(product.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderParkingAction>> GetActionsForProductAsync(
        string productId,
        CancellationToken cancellationToken = default) =>
        (await httpClient.GetFromJsonAsync<MockAction[]>(
            "api/actions?productId=" + Uri.EscapeDataString(productId),
            cancellationToken) ?? [])
            .Select(x => Map(x, productId))
            .ToArray();

    public async Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
        string providerProductId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var url = $"api/action-history?productId={Uri.EscapeDataString(providerProductId)}" +
                  $"&pageNumber={pageNumber}&pageSize={pageSize}";
        return await httpClient.GetFromJsonAsync<ProviderActionHistoryPage>(url, cancellationToken)
            ?? throw new InvalidOperationException("Parking provider returned no action history page.");
    }

    public async Task<ProviderParkingAction> StartActionAsync(
        ProviderParkingActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/actions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Map(
            await response.Content.ReadFromJsonAsync<MockAction>(cancellationToken)
                ?? throw new InvalidOperationException("Parking provider returned no action."),
            request.ProductId);
    }

    public async Task<ProviderParkingAction> ExtendActionAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        return await ExtendActionForProductAsync(product.Id, providerActionId, newEnd, cancellationToken);
    }

    public async Task<ProviderParkingAction> ExtendActionForProductAsync(
        string productId,
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/actions/{providerActionId}/end?productId={Uri.EscapeDataString(productId)}",
            new { End = newEnd },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return Map(
            await response.Content.ReadFromJsonAsync<MockAction>(cancellationToken)
                ?? throw new InvalidOperationException("Parking provider returned no action."),
            productId);
    }

    public async Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        await StopActionForProductAsync(product.Id, providerActionId, cancellationToken);
    }

    public async Task StopActionForProductAsync(
        string productId,
        string providerActionId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            $"api/actions/{providerActionId}/stop?productId={Uri.EscapeDataString(productId)}",
            null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static ProviderParkingAction Map(MockAction x, string? productId) =>
        new(x.Id, x.LicensePlate, x.Start, x.End, x.Location, x.Status, productId);

    private sealed record MockCategory(string Id, string Name);
    private sealed record MockProduct(string Id, string Name, string Location, string? CategoryId, string? CategoryName);
    private sealed record MockBalance(double RemainingPaidMinutes, DateTimeOffset RetrievedAt);
    private sealed record MockAction(string Id, string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location, string Status);
}
