using System.Globalization;
using System.Text.Json;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed record TwoParkHistoryAction(
    string ProviderActionId,
    string? Status,
    DateTime? StartLocal,
    DateTime? EndLocal,
    decimal? CostAmount,
    string? Currency);

public sealed record TwoParkHistoryPage(
    IReadOnlyList<TwoParkHistoryAction> Actions,
    int? StartIndex,
    int? StopIndex,
    int? MaxIndex);

public static class TwoParkActionHistoryParser
{
    private const string TimeFormat = "dd-MM-yyyy HH:mm:ss";

    public static TwoParkHistoryPage Parse(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return new([], null, null, null);

        var actions = new List<TwoParkHistoryAction>();
        if (data.TryGetProperty("actions", out var actionArray) && actionArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var action in actionArray.EnumerateArray())
            {
                if (!action.TryGetProperty("atn_id", out var idElement))
                    continue;

                var providerActionId = idElement.ToString();
                if (string.IsNullOrWhiteSpace(providerActionId))
                    continue;

                var parameters = ReadParameters(action);
                actions.Add(new TwoParkHistoryAction(
                    providerActionId,
                    ReadString(action, "atn_state"),
                    ReadDate(parameters, "TIMESTART"),
                    ReadDate(parameters, "TIMEEND"),
                    ReadAmount(parameters, "COST"),
                    ReadValue(parameters, "CURRENCY_DESC")));
            }
        }

        return new TwoParkHistoryPage(
            actions,
            ReadIndex(data, "startindex"),
            ReadIndex(data, "stopindex"),
            ReadIndex(data, "maxindex"));
    }

    private static Dictionary<string, string> ReadParameters(JsonElement action)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!action.TryGetProperty("atn_parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("prr_label", out var labelElement) ||
                !parameter.TryGetProperty("prr_value", out var valueElement))
                continue;

            var label = labelElement.ToString();
            if (!string.IsNullOrWhiteSpace(label))
                values[label] = valueElement.ToString();
        }

        return values;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadValue(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static DateTime? ReadDate(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value) ||
            !DateTime.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return null;

        return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
    }

    private static decimal? ReadAmount(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) &&
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int? ReadIndex(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) &&
        int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}