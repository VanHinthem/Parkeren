using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkProviderHistoryReaderTests
{
    [Fact]
    public async Task Reads_one_based_pages_and_maps_history_to_provider_records()
    {
        var handler = new HistoryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://twopark.test/") };
        var provider = CreateProvider(http);

        var firstPage = await provider.GetActionHistoryPageAsync("product-1", 0, 10, TestContext.Current.CancellationToken);
        var secondPage = await provider.GetActionHistoryPageAsync("product-1", 1, 10, TestContext.Current.CancellationToken);
        var lastPage = await provider.GetActionHistoryPageAsync("product-1", 2, 10, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { (1, 10), (11, 20), (21, 21) }, handler.RequestedRanges);
        Assert.Equal(10, firstPage.Records.Count);
        Assert.Equal(10, secondPage.Records.Count);
        Assert.Single(lastPage.Records);
        Assert.True(firstPage.HasMore);
        Assert.True(secondPage.HasMore);
        Assert.False(lastPage.HasMore);

        var record = firstPage.Records[0];
        Assert.Equal("action-1", record.ProviderActionId);
        Assert.Equal("COMPLETED", record.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T06:41:14+00:00", CultureInfo.InvariantCulture), record.ActualStartAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T06:41:27+00:00", CultureInfo.InvariantCulture), record.ActualEndAt);
        Assert.Equal(0.01m, record.ProviderCostAmount);
        Assert.Equal("€", record.Currency);
    }

    [Fact]
    public async Task Http_success_with_provider_error_status_throws_typed_provider_exception()
    {
        using var http = new HttpClient(new HistoryHandler(returnProviderError: true))
        {
            BaseAddress = new Uri("https://twopark.test/")
        };
        var provider = CreateProvider(http);

        var exception = await Assert.ThrowsAsync<TwoParkProviderException>(() =>
            provider.GetActionHistoryPageAsync("product-1", 0, 10, TestContext.Current.CancellationToken));

        Assert.Contains("PROVIDER_FAILURE", exception.Message, StringComparison.Ordinal);
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

    private sealed class HistoryHandler(bool returnProviderError = false) : HttpMessageHandler
    {
        public List<(int Start, int Stop)> RequestedRanges { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimStart('/');
            if (path == "check_credentials.json")
                return JsonResponse("{\"status\":{\"code\":{\"major\":\"OK\"}},\"data\":{}}");

            if (path != "get_action_history.json")
                throw new InvalidOperationException($"Unexpected 2Park request: {path}");

            var form = ParseForm(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("product-1", form["product_id"]);
            Assert.Equal("nl_NL", form["locale"]);
            var startIndex = int.Parse(form["startindex"], CultureInfo.InvariantCulture);
            var stopIndex = int.Parse(form["stopindex"], CultureInfo.InvariantCulture);
            RequestedRanges.Add((startIndex, stopIndex));
            if (returnProviderError)
                return JsonResponse("{\"status\":{\"code\":{\"major\":\"ERROR\",\"minor\":\"PROVIDER_FAILURE\"},\"message\":\"History unavailable\"}}");

            var actualStop = Math.Min(stopIndex, 21);
            var actions = Enumerable.Range(startIndex, Math.Max(0, actualStop - startIndex + 1))
                .Select(CreateActionJson);
            var actionJson = string.Join(",", actions);
            var response = $$"""
            {
              "status": { "code": { "major": "OK", "minor": "SUCCESS" }, "message": "Gelukt" },
              "data": {
                "startindex": "{{startIndex}}",
                "stopindex": "{{actualStop}}",
                "maxindex": "21",
                "actions": [{{actionJson}}]
              }
            }
            """;
            return JsonResponse(response);
        }

        private static string CreateActionJson(int index) =>
            $$"""
            {
              "atn_state": "COMPLETED",
              "atn_id": "action-{{index}}",
              "atn_parameters": [
                { "prr_label": "TIMESTART", "prr_value": "04-10-2026 08:41:14" },
                { "prr_label": "TIMEEND", "prr_value": "04-10-2026 08:41:27" },
                { "prr_label": "COST", "prr_value": "0.01" },
                { "prr_label": "CURRENCY_DESC", "prr_value": "€" }
              ]
            }
            """;

        private static Dictionary<string, string> ParseForm(string value) => value
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0].Replace('+', ' ')),
                part => Uri.UnescapeDataString(part[1].Replace('+', ' ')),
                StringComparer.Ordinal);

        private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}