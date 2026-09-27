using System.Net.Http.Json;
using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class TwoParkMockProvider(HttpClient httpClient) : IParkingProvider
{
    public async Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<MockBalance>("api/balance", cancellationToken)
            ?? throw new InvalidOperationException("Parking provider returned no balance.");
        return new ProviderBalance(TimeSpan.FromMinutes(response.RemainingPaidMinutes), response.RetrievedAt);
    }

    public async Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default) =>
        (await httpClient.GetFromJsonAsync<MockAction[]>("api/actions", cancellationToken) ?? [])
            .Select(Map).ToArray();

    public async Task<ProviderParkingAction> StartActionAsync(ProviderParkingActionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/actions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Map(await response.Content.ReadFromJsonAsync<MockAction>(cancellationToken)
            ?? throw new InvalidOperationException("Parking provider returned no action."));
    }

    public async Task<ProviderParkingAction> ExtendActionAsync(string providerActionId, DateTimeOffset newEnd, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"api/actions/{providerActionId}/end", new { End = newEnd }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Map(await response.Content.ReadFromJsonAsync<MockAction>(cancellationToken)
            ?? throw new InvalidOperationException("Parking provider returned no action."));
    }

    public async Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"api/actions/{providerActionId}/stop", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static ProviderParkingAction Map(MockAction x) =>
        new(x.Id, x.LicensePlate, x.Start, x.End, x.Location, x.Status);

    private sealed record MockBalance(double RemainingPaidMinutes, DateTimeOffset RetrievedAt);
    private sealed record MockAction(string Id, string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location, string Status);
}
