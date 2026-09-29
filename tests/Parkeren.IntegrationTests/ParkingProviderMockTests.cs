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

        var categories = await provider.GetCategoriesAsync(cancellationToken);
        Assert.Contains(categories, x => x.Id == "oss");
        var product = await provider.GetProductAsync(cancellationToken);
        Assert.Equal("Oss", product.Location);

        var balance = await provider.GetBalanceAsync(cancellationToken);
        Assert.True(balance.RemainingBalance > 0);
        Assert.Equal(ProviderBalanceUnit.Minute, balance.Unit);

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
    public async Task Mock_can_store_action_while_omitting_created_response_body()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/omit-created-body", new { Enabled = true }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("NOBODY1", start, start.AddHours(1), "Oss"), cancellationToken));

        Assert.Single(await provider.GetActionsAsync(cancellationToken), x => x.LicensePlate == "NOBODY1" && x.Status == "active");
    }

    [Fact]
    public async Task Mock_can_reject_duplicate_active_action_for_same_plate()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/reject-duplicates", new { Enabled = true }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        var request = new ProviderParkingActionRequest("DUP1", start, start.AddHours(1), "Oss");
        await provider.StartActionAsync(request, cancellationToken);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(request, cancellationToken));

        Assert.Single(await provider.GetActionsAsync(cancellationToken), x => x.LicensePlate == "DUP1" && x.Status == "active");
    }

    [Fact]
    public async Task Mock_can_simulate_action_stopped_externally()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        var created = await provider.StartActionAsync(new ProviderParkingActionRequest("EXT1", start, start.AddHours(1), "Oss"), cancellationToken);

        var externalStop = await http.PostAsync($"api/test/actions/{created.ProviderActionId}/stop", null, cancellationToken);
        externalStop.EnsureSuccessStatusCode();

        var action = Assert.Single(await provider.GetActionsAsync(cancellationToken), x => x.ProviderActionId == created.ProviderActionId);
        Assert.Equal("stopped", action.Status);
    }

    [Fact]
    public async Task Mock_can_return_provider_validation_error()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/validation-error", new { Enabled = true }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("INVALID1", start, start.AddHours(1), "Oss"), cancellationToken));
        Assert.Empty(await provider.GetActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Mock_can_delay_action_visibility_after_write()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/visibility-delay", new { Milliseconds = 100 }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        var created = await provider.StartActionAsync(new ProviderParkingActionRequest("DELAY1", start, start.AddHours(1), "Oss"), cancellationToken);

        Assert.DoesNotContain(await provider.GetActionsAsync(cancellationToken), x => x.ProviderActionId == created.ProviderActionId);
        await Task.Delay(150, cancellationToken);
        Assert.Contains(await provider.GetActionsAsync(cancellationToken), x => x.ProviderActionId == created.ProviderActionId);
    }

    [Fact]
    public async Task Mock_can_reject_invalid_provider_credentials()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/authentication", new { Valid = false }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetBalanceAsync(cancellationToken));
    }

    [Fact]
    public async Task Mock_rejects_action_when_provider_balance_is_insufficient()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/balance", new { RemainingPaidMinutes = 30 }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("BAL1", start, start.AddMinutes(31), "Oss"), cancellationToken));
    }

    [Fact]
    public async Task Mock_enforces_configured_maximum_action_duration()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var config = await http.PostAsJsonAsync("api/test/max-action-duration", new { Minutes = 30 }, cancellationToken);
        config.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("LONG1", start, start.AddMinutes(31), "Oss"), cancellationToken));
    }

    [Fact]
    public async Task Mock_enforces_configured_provider_capacity()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var capacity = await http.PostAsJsonAsync("api/test/capacity", new { MaxConcurrentActions = 1 }, cancellationToken);
        capacity.EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;
        await provider.StartActionAsync(new ProviderParkingActionRequest("CAP1", start, start.AddHours(1), "Oss"), cancellationToken);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("CAP2", start, start.AddHours(1), "Oss"), cancellationToken));
    }

    [Fact]
    public async Task Mock_state_can_be_reset_between_scenarios()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new TwoParkMockProvider(http);
        var start = DateTimeOffset.UtcNow;

        await provider.StartActionAsync(new ProviderParkingActionRequest("RESET1", start, start.AddHours(1), "Oss"), cancellationToken);
        var reset = await http.PostAsync("api/test/reset", null, cancellationToken);
        reset.EnsureSuccessStatusCode();

        Assert.Empty(await provider.GetActionsAsync(cancellationToken));
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
