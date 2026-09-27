using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests;

public sealed class ParkingProviderMockTests
{
    [Fact]
    public async Task Mock_adapter_supports_balance_start_read_extend_and_stop()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var provider = new TwoParkMockProvider(http);
        var cancellationToken = TestContext.Current.CancellationToken;

        var balance = await provider.GetBalanceAsync(cancellationToken);
        Assert.True(balance.RemainingPaidDuration > TimeSpan.Zero);

        var start = DateTimeOffset.UtcNow.AddMinutes(5);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("TK01HF", start, start.AddHours(2), "Oss"), cancellationToken);

        var actions = await provider.GetActionsAsync(cancellationToken);
        Assert.Contains(actions, x => x.ProviderActionId == created.ProviderActionId);

        var extended = await provider.ExtendActionAsync(created.ProviderActionId, start.AddHours(3), cancellationToken);
        Assert.Equal(start.AddHours(3), extended.End);

        await provider.StopActionAsync(created.ProviderActionId, cancellationToken);
        actions = await provider.GetActionsAsync(cancellationToken);
        Assert.Equal("stopped", actions.Single(x => x.ProviderActionId == created.ProviderActionId).Status);

    }

    [Fact]
    public async Task Mock_can_apply_action_before_returning_unknown_outcome()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var setup = await http.PostAsJsonAsync("api/test/unknown-outcome", new { StatusCode = 504, Count = 1 }, cancellationToken);
        setup.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("UNKNOWN1", start, start.AddHours(1), "Oss"), cancellationToken));

        var actions = await provider.GetActionsAsync(cancellationToken);
        Assert.Contains(actions, x => x.LicensePlate == "UNKNOWN1" && x.Status == "active");
    }

    [Fact]
    public async Task Mock_can_inject_provider_failure()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var setup = await http.PostAsJsonAsync("api/test/failure", new { StatusCode = 503, Count = 1 }, cancellationToken);
        setup.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("TK01HF", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "Oss"), cancellationToken));
    }
}
