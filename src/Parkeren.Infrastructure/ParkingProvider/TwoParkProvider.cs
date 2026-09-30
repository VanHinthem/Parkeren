using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Parkeren.Application.ParkingProvider;

namespace Parkeren.Infrastructure.ParkingProvider;

public sealed class TwoParkProvider(HttpClient httpClient, IConfiguration configuration) : IParkingProvider
{
    private const string Locale = "nl_NL";
    private const string TimeFormat = "dd-MM-yyyy HH:mm:ss";
    private static readonly TimeZoneInfo AmsterdamTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Amsterdam");

    private readonly string email = configuration["ParkingProvider:Email"]
        ?? throw new InvalidOperationException("ParkingProvider:Email is not configured.");
    private readonly string password = configuration["ParkingProvider:Password"]
        ?? throw new InvalidOperationException("ParkingProvider:Password is not configured.");
    private string? productId = configuration["ParkingProvider:ProductId"];
    private string? location = configuration["ParkingProvider:Location"];
    private bool authenticated;

    public async Task<IReadOnlyList<ProviderCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var data = await PostAsync("get_categories.json", new Dictionary<string, string> { ["locale"] = Locale }, cancellationToken);
        var result = new List<ProviderCategory>();
        foreach (var category in data.RootElement.GetProperty("data").GetProperty("categories").EnumerateArray())
        {
            var id = category.TryGetProperty("cty_id", out var idElement) ? idElement.ToString() : string.Empty;
            var name = category.TryGetProperty("cty_name", out var nameElement) ? nameElement.GetString() ?? id : id;
            result.Add(new ProviderCategory(id, name));
        }
        return result;
    }

    public async Task<ProviderProduct> GetProductAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        return new ProviderProduct(productId ?? string.Empty, "2Park", location ?? string.Empty);
    }

    public async Task<ProviderBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("get_balance.json", new Dictionary<string, string>
        {
            ["product_id"] = productId ?? string.Empty,
            ["locale"] = Locale
        }, cancellationToken);

        var (amount, unit) = ParseBalance(data.RootElement);
        return new ProviderBalance(amount, unit, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ProviderParkingAction>> GetActionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("get_category_product_details.json", new Dictionary<string, string>
        {
            ["product_id"] = productId ?? string.Empty,
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
                    : location ?? string.Empty;
                var state = action.TryGetProperty("atn_state", out var stateElement)
                    ? stateElement.GetString() ?? "unknown"
                    : "unknown";

                result.Add(new ProviderParkingAction(
                    id,
                    plate,
                    ParseProviderTime(startRaw),
                    ParseProviderTime(endRaw),
                    actionLocation,
                    state.ToLowerInvariant()));
            }
        }

        return result;
    }

    public async Task<ProviderParkingAction> StartActionAsync(
        ProviderParkingActionRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

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
            ["product_id"] = productId ?? string.Empty,
            ["locale"] = Locale,
            ["data"] = payload
        }, cancellationToken);

        var actions = await GetActionsAsync(cancellationToken);
        var match = actions
            .Where(x => string.Equals(x.LicensePlate, NormalizePlate(request.LicensePlate), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Start)
            .FirstOrDefault(x => (x.Start - request.Start).Duration() < TimeSpan.FromMinutes(2));

        if (match is null)
            throw new InvalidOperationException("2Park start was accepted but could not be verified by read-back.");

        return match;
    }

    public async Task<ProviderParkingAction> ExtendActionAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("extend_action.json", new Dictionary<string, string>
        {
            ["action_id"] = providerActionId,
            ["product_id"] = productId ?? string.Empty,
            ["locale"] = Locale,
            ["VALID_UNTIL"] = FormatProviderTime(newEnd)
        }, cancellationToken);

        var action = (await GetActionsAsync(cancellationToken)).SingleOrDefault(x => x.ProviderActionId == providerActionId);
        if (action is null)
            throw new InvalidOperationException("2Park extension was accepted but the action could not be verified by read-back.");
        return action;
    }

    public async Task<string> ExtendActionDiagnosticAsync(
        string providerActionId,
        DateTimeOffset newEnd,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("extend_action.json", new Dictionary<string, string>
        {
            ["action_id"] = providerActionId,
            ["product_id"] = productId ?? string.Empty,
            ["locale"] = Locale,
            ["VALID_UNTIL"] = FormatProviderTime(newEnd)
        }, cancellationToken);

        return data.RootElement.GetRawText();
    }

    public async Task StopActionAsync(string providerActionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var data = await PostAsync("stop_action.json", new Dictionary<string, string>
        {
            ["action_id"] = providerActionId,
            ["product_id"] = productId ?? string.Empty,
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
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(location))
            await DiscoverProductAsync(cancellationToken);
    }

    private async Task DiscoverProductAsync(CancellationToken cancellationToken)
    {
        using var categories = await PostAsync("get_categories.json", new Dictionary<string, string> { ["locale"] = Locale }, cancellationToken);
        foreach (var category in categories.RootElement.GetProperty("data").GetProperty("categories").EnumerateArray())
        {
            if (!category.TryGetProperty("cty_products", out var products))
                continue;

            foreach (var product in products.EnumerateArray())
            {
                var id = product.TryGetProperty("pdt_id", out var idElement) ? idElement.ToString() : null;
                var blocked = product.TryGetProperty("pdt_is_blocked", out var blockedElement)
                    && string.Equals(blockedElement.GetString(), "true", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(id) || blocked)
                    continue;

                productId = id;
                location ??= ExtractLocation(product);
                return;
            }
        }

        throw new InvalidOperationException("No usable 2Park product was found for this account.");
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
        throw new InvalidOperationException($"2Park request failed: {minor ?? "UNKNOWN"} {message}".Trim());
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
        var offset = AmsterdamTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
