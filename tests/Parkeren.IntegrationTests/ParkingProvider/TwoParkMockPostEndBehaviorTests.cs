using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkMockPostEndBehaviorTests
{
    [Fact]
    public async Task Post_end_behavior_is_only_applied_when_explicitly_configured()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var end = now.AddHours(1);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("POSTEND1", now, end, "Oss"),
            cancellationToken);

        (await http.PostAsJsonAsync("api/test/clock/advance", new { Milliseconds = 2 * 60 * 60 * 1000 }, cancellationToken)).EnsureSuccessStatusCode();

        var defaultReadback = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("active", defaultReadback.Status, ignoreCase: true);

        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "completed" }, cancellationToken)).EnsureSuccessStatusCode();
        var completed = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("completed", completed.Status, ignoreCase: true);

        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "keep-active" }, cancellationToken)).EnsureSuccessStatusCode();
        var active = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("active", active.Status, ignoreCase: true);

        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "hide" }, cancellationToken)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
    }

    [Fact]
    public async Task Explicit_stop_wins_over_post_end_hide_mode()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("POSTEND2", now, now.AddHours(1), "Oss"),
            cancellationToken);
        await provider.StopActionAsync(created.ProviderActionId, cancellationToken);

        (await http.PostAsJsonAsync("api/test/clock/advance", new { Milliseconds = 2 * 60 * 60 * 1000 }, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "hide" }, cancellationToken)).EnsureSuccessStatusCode();

        var stopped = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("stopped", stopped.Status, ignoreCase: true);
    }
}
