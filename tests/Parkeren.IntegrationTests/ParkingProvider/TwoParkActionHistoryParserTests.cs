using System.Text.Json;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkActionHistoryParserTests
{
    [Fact]
    public void Parses_history_fields_and_pagination_without_timezone_conversion()
    {
        var page = ParseFixture("two-park-action-history.json");

        var action = Assert.Single(page.Actions);
        Assert.Equal("ANONYMIZED-ACTION-001", action.ProviderActionId);
        Assert.Equal("COMPLETED", action.Status);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 36, 36, DateTimeKind.Unspecified), action.StartLocal);
        Assert.Equal(new DateTime(2026, 10, 3, 11, 36, 47, DateTimeKind.Unspecified), action.EndLocal);
        Assert.Equal(0.01m, action.CostAmount);
        Assert.Equal("EUR", action.Currency);
        Assert.Equal("XX-00-XX", action.LicensePlate);
        Assert.Equal("OSS Zone J", action.Location);
        Assert.Equal(10, page.StartIndex);
        Assert.Equal(19, page.StopIndex);
        Assert.Equal(20, page.MaxIndex);
    }

    [Fact]
    public void Keeps_record_with_missing_or_unusable_fields_as_incomplete()
    {
        var page = ParseFixture("two-park-action-history-incomplete.json");

        var action = Assert.Single(page.Actions);
        Assert.Equal("ANONYMIZED-ACTION-INCOMPLETE", action.ProviderActionId);
        Assert.Equal("COMPLETED", action.Status);
        Assert.Null(action.StartLocal);
        Assert.Null(action.CostAmount);
        Assert.Null(action.Currency);
        Assert.Null(action.LicensePlate);
        Assert.Null(action.Location);
        Assert.Null(page.StartIndex);
        Assert.Null(page.StopIndex);
        Assert.Null(page.MaxIndex);
    }

    [Fact]
    public void Missing_data_or_actions_returns_an_empty_page()
    {
        using var document = JsonDocument.Parse("{\"status\":{\"code\":{\"major\":\"OK\"}},\"data\":{}}");

        var page = TwoParkActionHistoryParser.Parse(document.RootElement);

        Assert.Empty(page.Actions);
        Assert.Null(page.StartIndex);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\\"atn_id\\":\\"\\"}")]
    public void Rejects_history_actions_without_valid_provider_id(string actionJson)
    {
        using var document = JsonDocument.Parse(
            "{\\"data\\":{\\"actions\\":[" + actionJson + "]}}");

        Assert.Throws<JsonException>(() =>
            TwoParkActionHistoryParser.Parse(document.RootElement));
    }

    private static TwoParkHistoryPage ParseFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", name);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return TwoParkActionHistoryParser.Parse(document.RootElement);
    }
}