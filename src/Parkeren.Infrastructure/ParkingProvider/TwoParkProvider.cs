using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class TwoParkProvider(HttpClient httpClient, IConfiguration configuration) : IParkingProvider, IProviderActionHistoryReader
{
    private const string Locale = "nl_NL";
    private const string TimeFormat = "dd-MM-yyyy HH:mm:ss";
    private const int MaxActionHistoryPageSize = 10;
    private static readonly TimeZoneInfo AmsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Amsterdam");
    private readonly ConcurrentDictionary<string, int> historyMaxIndexByProduct = new(StringComparer.Ordinal);

    private readonly string email = configuration["ParkingProvider:Email"]
        ?? throw new InvalidOperationException("ParkingProvider:Email is not configured.");
    private readonly string password = configuration["ParkingProvider:Password"]
        ?? throw new InvalidOperationException("ParkingProvider:Password is not configured.");
    private readonly string? configuredProductId = configuration["ParkingProvider:ProductId"];
    private bool authenticated;

    public async Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await GetCategoriesDocumentAsync(cancellationToken);
        var result = new List<ProviderCategory>();

        foreach (var category in data.RootElement.GetProperty("data").GetProperty("categories").EnumerateArray())
        {
            var id = category.TryGetProperty("cty_id", out var idElement) ? idElement.ToString() : string.Empty;
            var name = category.TryGetProperty("cty_name", out var nameElement) ? nameElement.GetString() ?? id : id;
            result.Add(new ProviderCategory(id, name));
        }

        return result;
    }

    public async Task<IReadOnlyList<ProviderProduct>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await GetCategoriesDocumentAsync(cancellationToken);
        var result = new List<ProviderProduct>();

        foreach (var category in data.RootElement.GetProperty("data").GetProperty("categories").EnumerateArray())
        {
            var categoryId = category.TryGetProperty("cty_id", out var categoryIdElement)
                ? categoryIdElement.ToString()
                : null;
            var categoryName = category.TryGetProperty("cty_name", out var categoryNameElement)
                ? categoryNameElement.GetString()
                : null;

            if (!category.TryGetProperty("cty_products", out var products))
                continue;

            foreach (var product in products.EnumerateArray())
            {
                var id = product.TryGetProperty("pdt_id", out var idElement)
                    ? idElement.ToString()
                    : null;
                if (string.IsNullOrWhiteSpace(id) || IsBlocked(product))
                    continue;

                var name = product.TryGetProperty("pdt_name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                name = string.IsNullOrWhiteSpace(name) ? id : name;

                var location = ExtractLocation(product);
                if (string.IsNullOrWhiteSpace(location))
                    location = await DiscoverLocationAsync(id, cancellationToken);
                if (string.IsNullOrWhiteSpace(location))
                    throw new InvalidOperationException($"2Park product '{id}' has no usable LOCATION.");

                result.Add(new ProviderProduct(
                    id,
                    name,
                    location,
                    categoryId,
                    categoryName));
            }
        }

        return result;
    }

    public async Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetProductsAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(configuredProductId))
        {
            return products.SingleOrDefault(x => string.Equals(x.Id, configuredProductId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Configured 2Park product '{configuredProductId}' was not returned by the provider.");
        }

        return products.Count switch
        {
            1 => products[0],
            0 => throw new InvalidOperationException("No usable 2Park product was found for this account."),
            _ => throw new InvalidOperationException("Multiple 2Park products are available; an explicit product selection is required.")
        };
    }

    public async Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        return await GetBalanceForProductAsync(product.Id, cancellationToken);
    }

    public async Task<ProviderBalance> GetBalanceForProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product id is required.", nameof(productId));

        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("get_balance.json", new Dictionary<string, string>
        {
            ["product_id"] = productId,
            ["locale"] = Locale
        }, cancellationToken);

        var (amount, unit) = ParseBalance(data.RootElement);
        return new ProviderBalance(amount, unit, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        return await GetActionsForProductAsync(product.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderParkingAction>> GetActionsForProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("Product id is required.", nameof(productId));

        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("get_category_product_details.json", new Dictionary<string, string>
        {
            ["product_id"] = productId,
            ["locale"] = Locale
        }, cancellationToken);

        var result = new List<ProviderParkingAction>();
        if (!data.RootElement.GetProperty("data").TryGetProperty("pdt_members", out var members))
            return result;

        foreach (var member in members.EnumerateArray())
        {
            var memberPlate = member.TryGetProperty("mbr_identifier", out var plateElement)
                ? NormalizePlate(plateElement.GetString() ?? string.Empty)
                : string.Empty;
            if (!member.TryGetProperty("mbr_actions", out var actions))
                continue;

            foreach (var action in actions.EnumerateArray())
            {
                var id = action.TryGetProperty("atn_id", out var idElement) ? idElement.ToString() : string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var parameters = ReadParameters(action, "atn_parameters");
                if (!parameters.TryGetValue("TIMESTART", out var startRaw) ||
                    !parameters.TryGetValue("TIMEEND", out var endRaw))
                    continue;

                var plate = parameters.TryGetValue("MBR_IDENT", out var plateRaw)
                    ? NormalizePlate(plateRaw)
                    : memberPlate;
                var actionLocation = parameters.TryGetValue("LOCATION", out var locationRaw)
                    ? locationRaw
                    : string.Empty;
                var state = action.TryGetProperty("atn_state", out var stateElement)
                    ? stateElement.GetString() ?? "unknown"
                    : "unknown";

                result.Add(new ProviderParkingAction(
                    id,
                    plate,
                    ParseProviderTime(startRaw),
                    ParseProviderTime(endRaw),
                    actionLocation,
                    state.ToLowerInvariant(),
                    productId));
            }
        }

        return result;
    }

    public async Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
        string providerProductId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerProductId))
            throw new ArgumentException("Provider product id is required.", nameof(providerProductId));
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (pageSize is < 1 or > MaxActionHistoryPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"2Park supports history pages of up to {MaxActionHistoryPageSize} records.");

        var startIndex = checked(pageNumber * pageSize + 1);
        var stopIndex = checked(startIndex + pageSize - 1);
        if (historyMaxIndexByProduct.TryGetValue(providerProductId, out var maxIndex))
        {
            if (startIndex > maxIndex)
                return new ProviderActionHistoryPage([], pageNumber, pageSize, maxIndex);
            stopIndex = Math.Min(stopIndex, maxIndex);
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("get_action_history.json", new Dictionary<string, string>
        {
            ["product_id"] = providerProductId,
            ["locale"] = Locale,
            ["startindex"] = startIndex.ToString(CultureInfo.InvariantCulture),
            ["stopindex"] = stopIndex.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        var historyPage = TwoParkActionHistoryParser.Parse(data.RootElement);
        if (historyPage.MaxIndex is not int pageMaxIndex || pageMaxIndex < 0)
            throw new JsonException("2Park action history response has no valid maxindex.");

        historyMaxIndexByProduct[providerProductId] = pageMaxIndex;

        // Live 2Park has returned 21..23 for a 21..24 request while index 24
        // remained retrievable on its own. Recover only an omitted terminal
        // index; do not change the established normal page ranges.
        var actions = historyPage.Actions.ToList();
        if (stopIndex == pageMaxIndex &&
            historyPage.StopIndex is int actualStop &&
            actualStop >= startIndex - 1 &&
            actualStop < stopIndex)
        {
            for (var index = actualStop + 1; index <= stopIndex; index++)
            {
                using var tailData = await PostAsync("get_action_history.json",
                    new Dictionary<string, string>
                    {
                        ["product_id"] = providerProductId,
                        ["locale"] = Locale,
                        ["startindex"] = index.ToString(CultureInfo.InvariantCulture),
                        ["stopindex"] = index.ToString(CultureInfo.InvariantCulture)
                    }, cancellationToken);
                var tail = TwoParkActionHistoryParser.Parse(tailData.RootElement);
                if (tail.MaxIndex != pageMaxIndex ||
                    tail.StartIndex != index ||
                    tail.StopIndex != index ||
                    tail.Actions.Count != 1)
                    throw new JsonException("2Park history terminal index could not be verified.");
                // Provider stopindex metadata can understate the last action
                // already present in the range. Never introduce a duplicate ID.
                if (!actions.Any(x => string.Equals(
                        x.ProviderActionId, tail.Actions[0].ProviderActionId, StringComparison.Ordinal)))
                    actions.Add(tail.Actions[0]);
            }
        }

        var records = actions
            .Where(x => x.StartLocal.HasValue && x.EndLocal.HasValue)
            .Select(x => new ProviderActionHistoryRecord(
                x.ProviderActionId,
                x.Status ?? string.Empty,
                ParseProviderTime(x.StartLocal!.Value),
                ParseProviderTime(x.EndLocal!.Value),
                x.CostAmount,
                x.Currency,
                x.LicensePlate is null ? null : NormalizePlate(x.LicensePlate),
                x.Location))
            .ToArray();

        return new ProviderActionHistoryPage(records, pageNumber, pageSize, pageMaxIndex);
    }

    public async Task<ProviderParkingAction> StartActionAsync(
        ProviderParkingActionRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var selectedProductId = request.ProductId;
        if (string.IsNullOrWhiteSpace(selectedProductId))
            selectedProductId = (await GetProductAsync(cancellationToken)).Id;

        var payload = JsonSerializer.Serialize(new
        {
            action = new
            {
                atn_parameters = new object[]
                {
                    new { prr_label = "MBR_IDENT", prr_value = NormalizePlate(request.LicensePlate) },
                    new { prr_label = "TIMESTART", prr_value = FormatProviderTime(request.Start) },
                    new { prr_label = "TIMEEND", prr_value = FormatProviderTime(request.End) },
                    new { prr_label = "LOCATION", prr_value = request.Location }
                }
            }
        });

        using var data = await PostAsync("start_action.json", new Dictionary<string, string>
        {
            ["product_id"] = selectedProductId,
            ["locale"] = Locale,
            ["data"] = payload
        }, cancellationToken);

        var actions = await GetActionsForProductAsync(selectedProductId, cancellationToken);
        var match = ProviderActionMatchPolicy.FindUniqueMatch(
            actions,
            new ProviderActionMatchCriteria(
                ProviderActionId: null,
                ProviderProductId: selectedProductId,
                LicensePlate: request.LicensePlate,
                AllowedStatuses: ["active", "scheduled"],
                ExpectedStart: request.Start,
                ExpectedEnd: request.End));

        if (match is null)
            throw new InvalidOperationException("2Park start was accepted but could not be verified by read-back.");

        return match;
    }

    public async Task<ProviderParkingAction> ExtendActionAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        return await ExtendActionForProductAsync(product.Id, providerActionId, newEnd, cancellationToken);
    }

    public async Task<ProviderParkingAction> ExtendActionForProductAsync(
        string productId,
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("extend_action.json", new Dictionary<string, string>
        {
            ["action_id"] = providerActionId,
            ["product_id"] = productId,
            ["locale"] = Locale,
            ["VALID_UNTIL"] = FormatProviderTime(newEnd)
        }, cancellationToken);

        var action = (await GetActionsForProductAsync(productId, cancellationToken))
            .SingleOrDefault(x => x.ProviderActionId == providerActionId);
        if (action is null)
            throw new InvalidOperationException("2Park extension was accepted but the action could not be verified by read-back.");
        return action;
    }

    public async Task<string> ExtendActionDiagnosticAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("extend_action.json", new Dictionary<string, string>
        {
            ["action_id"] = providerActionId,
            ["product_id"] = product.Id,
            ["locale"] = Locale,
            ["VALID_UNTIL"] = FormatProviderTime(newEnd)
        }, cancellationToken);

        return data.RootElement.GetRawText();
    }

    public async Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(cancellationToken);
        await StopActionForProductAsync(product.Id, providerActionId, cancellationToken);
    }

    public async Task StopActionForProductAsync(
        string productId,
        string providerActionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("stop_action.json", new Dictionary<string, string>
        {
            ["action_id"] = providerActionId,
            ["product_id"] = productId,
            ["locale"] = Locale
        }, cancellationToken);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (authenticated)
            return;

        using var response = await PostAsync("check_credentials.json", new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
            ["locale"] = Locale
        }, cancellationToken, allowReauthenticate: false);

        authenticated = true;
    }

    private Task<JsonDocument> GetCategoriesDocumentAsync(CancellationToken cancellationToken) =>
        PostAsync("get_categories.json", new Dictionary<string, string> { ["locale"] = Locale }, cancellationToken);

    private async Task<string?> DiscoverLocationAsync(string productId, CancellationToken cancellationToken)
    {
        try
        {
            using var data = await PostAsync("get_product_locations.json", new Dictionary<string, string>
            {
                ["locale"] = Locale,
                ["product_id"] = productId,
                ["location"] = string.Empty
            }, cancellationToken);

            if (!data.RootElement.GetProperty("data").TryGetProperty("locations", out var locations))
                return null;

            foreach (var location in locations.EnumerateArray())
            {
                var parameters = ReadParameters(location, "ltn_parameters");
                if (parameters.TryGetValue("LOCATION", out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch (InvalidOperationException)
        {
            // Some 2Park products already expose LOCATION directly in get_categories.
        }

        return null;
    }

    private async Task<JsonDocument> PostAsync(
        string path,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken,
        bool allowReauthenticate = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var document = JsonDocument.Parse(json);
        var major = document.RootElement.GetProperty("status").GetProperty("code").GetProperty("major").GetString();
        if (string.Equals(major, "OK", StringComparison.OrdinalIgnoreCase))
            return document;

        var minor = document.RootElement.GetProperty("status").GetProperty("code").TryGetProperty("minor", out var minorElement)
            ? minorElement.GetString()
            : null;

        if (allowReauthenticate && string.Equals(minor, "SESSION_TIMEOUT", StringComparison.OrdinalIgnoreCase))
        {
            document.Dispose();
            authenticated = false;
            await EnsureAuthenticatedAsync(cancellationToken);
            return await PostAsync(path, form, cancellationToken, allowReauthenticate: false);
        }

        var message = document.RootElement.GetProperty("status").TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;
        document.Dispose();
        throw new TwoParkProviderException(
            minor,
            message,
            $"2Park request failed: {minor ?? "UNKNOWN"} {message}".Trim());
    }

    private static bool IsBlocked(JsonElement product)
    {
        if (!product.TryGetProperty("pdt_is_blocked", out var blockedElement))
            return false;

        return blockedElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(blockedElement.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static Dictionary<string, string> ReadParameters(JsonElement element, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty(propertyName, out var parameters))
            return result;

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("prr_label", out var labelElement) ||
                !parameter.TryGetProperty("prr_value", out var valueElement))
                continue;
            var label = labelElement.GetString();
            var value = valueElement.GetString();
            if (!string.IsNullOrWhiteSpace(label) && value is not null)
                result[label] = value;
        }

        return result;
    }

    private static string? ExtractLocation(JsonElement product)
    {
        if (product.TryGetProperty("pdt_parameter_groups", out var groups))
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (group.TryGetProperty("pgp_label", out var groupLabel) &&
                    !string.Equals(groupLabel.GetString(), "START", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!group.TryGetProperty("pgp_parameters", out var parameters))
                    continue;

                foreach (var parameter in parameters.EnumerateArray())
                {
                    if (!parameter.TryGetProperty("prr_label", out var labelElement) ||
                        !string.Equals(labelElement.GetString(), "LOCATION", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (parameter.TryGetProperty("prr_value", out var valueElement) &&
                        !string.IsNullOrWhiteSpace(valueElement.GetString()))
                        return valueElement.GetString();
                    if (parameter.TryGetProperty("prr_default_value", out var defaultElement) &&
                        !string.IsNullOrWhiteSpace(defaultElement.GetString()))
                        return defaultElement.GetString();
                }
            }
        }

        if (product.TryGetProperty("pdt_id", out var idElement))
        {
            var match = Regex.Match(idElement.GetString() ?? string.Empty, @"^([A-Z]{3})\w+_(\d+)\$");
            if (match.Success)
                return $"{match.Groups[1].Value}{match.Groups[2].Value}";
        }

        return null;
    }

    private static (decimal Amount, ProviderBalanceUnit Unit) ParseBalance(JsonElement root)
    {
        if (!root.GetProperty("data").TryGetProperty("balance", out var balance) ||
            !balance.TryGetProperty("ble_parameters", out var parameters))
            return (0m, ProviderBalanceUnit.Unknown);

        decimal amount = 0m;
        string? rawUnit = null;
        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("prr_label", out var labelElement) ||
                !parameter.TryGetProperty("prr_value", out var valueElement))
                continue;

            var label = labelElement.GetString();
            var value = valueElement.GetString();
            if (string.Equals(label, "AMOUNT", StringComparison.OrdinalIgnoreCase))
                decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
            else if (string.Equals(label, "CURRENCY_CODE", StringComparison.OrdinalIgnoreCase))
                rawUnit = value;
        }

        var unit = rawUnit?.ToUpperInvariant() switch
        {
            "EURO" => ProviderBalanceUnit.Euro,
            "TIMES" => ProviderBalanceUnit.Times,
            "MINUTE" => ProviderBalanceUnit.Minute,
            _ => ProviderBalanceUnit.Unknown
        };
        return (amount, unit);
    }

    private static string NormalizePlate(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string FormatProviderTime(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, AmsterdamTimeZone);
        return local.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseProviderTime(string value)
    {
        var local = DateTime.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return ParseProviderTime(local);
    }

    private static DateTimeOffset ParseProviderTime(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = AmsterdamTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
