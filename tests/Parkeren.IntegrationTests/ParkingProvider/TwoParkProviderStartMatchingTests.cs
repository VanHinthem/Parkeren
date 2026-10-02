using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkProviderStartMatchingTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-02T12:00:00+00:00");
    private static readonly DateTimeOffset End = Start.AddHours(1);

    [Fact]
    public async Task Start_readback_accepts_unique_scheduled_action_within_central_tolerance()
    {
        using var http = CreateClient(DetailsJson(
            ActionJson("provider-1", "scheduled", "02-10-2026 14:00:04", "02-10-2026 15:00:04")));
        var provider = CreateProvider(http);

        var result = await provider.StartActionAsync(
            new ProviderParkingActionRequest("TK-01-HF", Start, End, "OSS_J", "product-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal("provider-1", result.ProviderActionId);
        Assert.Equal("scheduled", result.Status);
        Assert.Equal("OSS Zone J", result.Location);
    }

    [Fact]
    public async Task Start_readback_rejects_action_outside_central_tolerance()
    {
        using var http = CreateClient(DetailsJson(
            ActionJson("provider-1", "active", "02-10-2026 14:00:06", "02-10-2026 15:00:00")));
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("TK01HF", Start, End, "OSS_J", "product-1"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Start_readback_rejects_ambiguous_fallback_candidates()
    {
        using var http = CreateClient(DetailsJson(
            ActionJson("provider-1", "active", "02-10-2026 14:00:01", "02-10-2026 15:00:01"),
            ActionJson("provider-2", "scheduled", "02-10-2026 14:00:02", "02-10-2026 15:00:02")));
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartActionAsync(
            new ProviderParkingActionRequest("TK01HF", Start, End, "OSS_J", "product-1"),
            TestContext.Current.CancellationToken));
    }

    private static TwoParkProvider CreateProvider(HttpClient http)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ParkingProvider:Email"] = "test@example.com",
                ["ParkingProvider:Password"] = "secret"
            })
            .Build();
        return new TwoParkProvider(http, configuration);
    }

    private static HttpClient CreateClient(string detailsJson) =>
        new(new TwoParkHandler(detailsJson))
        {
            BaseAddress = new Uri("https://twopark.test/")
        };

    private static string DetailsJson(params string[] actions) =>
        $$"""
        {
          "status": { "code": { "major": "OK" } },
          "data": {
            "pdt_members": [
              {
                "mbr_identifier": "TK01HF",
                "mbr_actions": [{{string.Join(",", actions)}}]
              }
            ]
          }
        }
        """;

    private static string ActionJson(string id, string status, string start, string end) =>
        $$"""
        {
          "atn_id": "{{id}}",
          "atn_state": "{{status}}",
          "atn_parameters": [
            { "prr_label": "MBR_IDENT", "prr_value": "TK01HF" },
            { "prr_label": "TIMESTART", "prr_value": "{{start}}" },
            { "prr_label": "TIMEEND", "prr_value": "{{end}}" },
            { "prr_label": "LOCATION", "prr_value": "OSS Zone J" }
          ]
        }
        """;

    private sealed class TwoParkHandler(string detailsJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimStart('/');
            var body = path switch
            {
                "check_credentials.json" => OkJson,
                "start_action.json" => OkJson,
                "get_category_product_details.json" => detailsJson,
                _ => throw new InvalidOperationException($"Unexpected 2Park request: {path}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private const string OkJson = "{\"status\":{\"code\":{\"major\":\"OK\"}},\"data\":{}}";
    }
}
