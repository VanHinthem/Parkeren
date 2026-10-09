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
        Assert.Equal("AB12CD", record.LicensePlate);
        Assert.Equal("OSS Zone J", record.Location);
    }

    [Fact]
    public async Task History_only_validation_uses_authentication_and_read_only_history_endpoints()
    {
        var handler = new HistoryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://twopark.test/") };
        var provider = CreateProvider(http);

        await provider.GetActionHistoryPageAsync("product-1", 0, 10, TestContext.Current.CancellationToken);
        await provider.GetActionHistoryPageAsync("product-1", 1, 10, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["check_credentials.json", "get_action_history.json", "get_action_history.json"],
            handler.RequestedEndpoints);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 4)]
    public async Task Last_page_preserves_provider_maxindex_when_response_ends_before_requested_stop(
        bool terminalIdAlreadyPresent, int expectedCount)
    {
        var handler = new HistoryHandler(shortFinalPage: true, terminalIdAlreadyPresent: terminalIdAlreadyPresent);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://twopark.test/") };
        var provider = CreateProvider(http);

        var first = await provider.GetActionHistoryPageAsync(
            "product-1", 0, 10, TestContext.Current.CancellationToken);
        var second = await provider.GetActionHistoryPageAsync(
            "product-1", 1, 10, TestContext.Current.CancellationToken);
        var last = await provider.GetActionHistoryPageAsync(
            "product-1", 2, 10, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { (1, 10), (11, 20), (21, 24), (20, 24), (24, 24) }, handler.RequestedRanges);
        Assert.Equal(10, first.Records.Count);
        Assert.Equal(10, second.Records.Count);
        Assert.Equal(expectedCount, last.Records.Count);
        Assert.Equal(24, last.TotalCount);
        Assert.False(last.HasMore);
        Assert.Equal(expectedCount, last.Records.Select(x => x.ProviderActionId).Distinct().Count());
        Assert.Contains(last.Records, x => x.ProviderActionId == (terminalIdAlreadyPresent ? "action-23" : "action-24"));
    }

    [Fact]
    public async Task Live_boundary_shape_recovers_24_unique_ids_with_overlapping_terminal_page()
    {
        var handler = new HistoryHandler(liveBoundaryShape: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://twopark.test/") };
        var provider = CreateProvider(http);
        var ct = TestContext.Current.CancellationToken;

        var first = await provider.GetActionHistoryPageAsync("product-1", 0, 10, ct);
        var second = await provider.GetActionHistoryPageAsync("product-1", 1, 10, ct);
        var last = await provider.GetActionHistoryPageAsync("product-1", 2, 10, ct);
        var standardIds = first.Records.Concat(second.Records).Concat(last.Records)
            .Select(x => x.ProviderActionId).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(24, standardIds.Count);
        Assert.False(last.HasMore);
        Assert.Equal(5, last.Records.Count);
        Assert.Equal(new[] { (1, 10), (11, 20), (21, 24), (20, 24) },
            handler.RequestedRanges);
        Assert.Contains("action-21", standardIds);
        Assert.Contains("action-24", standardIds);
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
        Assert.Equal("PROVIDER_FAILURE", exception.ProviderCode);
        Assert.Equal("History unavailable", exception.ProviderMessage);
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

    private sealed class HistoryHandler(bool returnProviderError = false, bool shortFinalPage = false, bool terminalIdAlreadyPresent = false, bool liveBoundaryShape = false) : HttpMessageHandler
    {
        public List<(int Start, int Stop)> RequestedRanges { get; } = [];
        public List<string> RequestedEndpoints { get; } = [];
        public async Task<IReadOnlyList<string>> ReadRangeAsync(int start, int stop, CancellationToken ct)
        {
            using var response = await SendAsync(new HttpRequestMessage(
                HttpMethod.Post, "https://twopark.test/get_action_history.json")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["product_id"] = "product-1", ["locale"] = "nl_NL",
                    ["startindex"] = start.ToString(CultureInfo.InvariantCulture),
                    ["stopindex"] = stop.ToString(CultureInfo.InvariantCulture)
                })
            }, ct);
            using var payload = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct));
            return payload.RootElement.GetProperty("data").GetProperty("actions")
                .EnumerateArray().Select(x => x.GetProperty("atn_id").GetString()!).ToArray();
        }


        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimStart('/');
            if (path is not null)
                RequestedEndpoints.Add(path);
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

            var actualStop = liveBoundaryShape
                ? (startIndex == 21 ? 23 : Math.Min(stopIndex, 24))
                : Math.Min(stopIndex, shortFinalPage ? (startIndex == 24 ? 24 : 23) : 21);
            var actions = Enumerable.Range(startIndex, Math.Max(0, actualStop - startIndex + 1))
                .Select(index => CreateActionJson(
                    liveBoundaryShape && startIndex == 21
                        ? (index == 23 ? 24 : index + 1)
                        : terminalIdAlreadyPresent && startIndex == 24 ? 23 : index));
            var actionJson = string.Join(",", actions);
            var response = $$"""
            {
              "status": { "code": { "major": "OK", "minor": "SUCCESS" }, "message": "Gelukt" },
              "data": {
                "startindex": "{{startIndex}}",
                "stopindex": "{{actualStop}}",
                "maxindex": "{{(liveBoundaryShape ? 24 : shortFinalPage ? 24 : 21)}}",
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
                { "prr_label": "MBR_IDENT", "prr_value": "AB-12-CD" },
                { "prr_label": "LOCATION", "prr_value": "OSS Zone J" },
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