using Microsoft.AspNetCore.Mvc.Testing;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests;

public sealed class ParkingProviderMockTests
{
    [Fact]
    public async Task Mock_adapter_supports_balance_start_read_extend_and_stop()
    {
        await using var factory = new WebApplicationFactory<global::Program>();
        using var http = factory.CreateClient();
        var provider = new TwoParkMockProvider(http);

        var balance = await provider.GetBalanceAsync();
        Assert.True(balance.RemainingPaidDuration > TimeSpan.Zero);

        var start = DateTimeOffset.UtcNow.AddMinutes(5);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("TK01HF", start, start.AddHours(2), "Oss"));

        var actions = await provider.GetActionsAsync();
        Assert.Contains(actions, x => x.ProviderActionId == created.ProviderActionId);

        var extended = await provider.ExtendActionAsync(created.ProviderActionId, start.AddHours(3));
        Assert.Equal(start.AddHours(3), extended.End);

        await provider.StopActionAsync(created.ProviderActionId);
        actions = await provider.GetActionsAsync();
        Assert.Equal("stopped", actions.Single(x => x.ProviderActionId == created.ProviderActionId).Status);
    }
}
