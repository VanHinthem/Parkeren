using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkMockClockVisibilityTests
{
    [Fact]
    public async Task Visibility_delay_is_controlled_by_mock_clock()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/visibility-delay", new { Milliseconds = 10 * 60 * 1000 }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("CLOCKVIS1", now, now.AddHours(1), "Oss"),
            cancellationToken);

        Assert.DoesNotContain(
            await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);

        (await http.PostAsJsonAsync(
            "api/test/clock/advance",
            new { Milliseconds = 10 * 60 * 1000 },
            cancellationToken)).EnsureSuccessStatusCode();

        Assert.Contains(
            await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
    }
}
