using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkMockReadbackLocationTests
{
    [Fact]
    public async Task Readback_location_label_does_not_change_stored_mutation_location()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("LOCLBL1", now, now.AddHours(1), "OSS_J"),
            cancellationToken);
        Assert.Equal("OSS_J", created.Location);

        (await http.PostAsJsonAsync(
            "api/test/readback-location",
            new { Location = "OSS Zone J" },
            cancellationToken)).EnsureSuccessStatusCode();

        var readback = Assert.Single(
            await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("OSS Zone J", readback.Location);

        (await http.PostAsJsonAsync(
            "api/test/readback-location",
            new { Location = (string?)null },
            cancellationToken)).EnsureSuccessStatusCode();

        var restored = Assert.Single(
            await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("OSS_J", restored.Location);
    }
}
