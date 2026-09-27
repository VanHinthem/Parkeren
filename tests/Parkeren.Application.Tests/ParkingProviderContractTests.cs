using Parkeren.Application.ParkingProvider;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class ParkingProviderContractTests
{
    [Fact]
    public void Action_request_keeps_provider_neutral_values()
    {
        var start = DateTimeOffset.Parse("2026-09-28T07:00:00+00:00");
        var end = start.AddHours(4);

        var request = new ProviderParkingActionRequest("TK01HF", start, end, "Oss");

        Assert.Equal("TK01HF", request.LicensePlate);
        Assert.Equal(start, request.Start);
        Assert.Equal(end, request.End);
        Assert.Equal("Oss", request.Location);
    }
}
