using System.Text.Json;
using Parkeren.Domain.Notifications;

namespace Parkeren.Infrastructure.Notifications;

public sealed record WebPushMessage(string Title, string Body, string Url, Guid NotificationId);

public static class WebPushMessageFactory
{
    public static WebPushMessage? Create(Notification notification)
    {
        var visitUrl = notification.VisitId is Guid visitId
            ? $"/acties/{visitId}"
            : "/meldingen";

        return notification.Type switch
        {
            NotificationType.VisitStarted => new("Parkeren gestart", "De parkeeractie is gestart.", "/", notification.Id),
            NotificationType.VisitStopped => new("Parkeren gestopt", "De parkeeractie is gestopt.", visitUrl, notification.Id),
            NotificationType.ProviderContinuationSucceeded => new("Parkeren voortgezet", "De parkeeractie is automatisch voortgezet.", "/", notification.Id),
            NotificationType.ProviderContinuationAttentionRequired => CreateProviderAttention(notification, visitUrl),
            NotificationType.LongVisitWarning => CreateLongVisit(notification, visitUrl),
            NotificationType.BudgetWarning => CreateBudgetWarning(notification, "/meldingen"),
            _ => null
        };
    }

    public static string Serialize(WebPushMessage message) =>
        JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = message.Url,
            notificationId = message.NotificationId
        });

    private static WebPushMessage CreateProviderAttention(Notification notification, string url)
    {
        var reason = ReadString(notification.Payload, "Reason");
        return string.Equals(reason, "ExternalStop", StringComparison.OrdinalIgnoreCase)
            ? new("Parkeeractie extern gestopt", "2Park heeft deze parkeeractie buiten de app gestopt.", "/", notification.Id)
            : new("Parkeren vraagt aandacht", "De parkeeractie kon niet automatisch worden voortgezet.", url, notification.Id);
    }

    private static WebPushMessage CreateLongVisit(Notification notification, string url)
    {
        var licensePlate = ReadString(notification.Payload, "LicensePlate");
        var body = string.IsNullOrWhiteSpace(licensePlate)
            ? "Een parkeeractie duurt langer dan ingesteld."
            : $"Parkeeractie {licensePlate} duurt langer dan ingesteld.";
        return new("Langdurig parkeren", body, url, notification.Id);
    }

    private static WebPushMessage CreateBudgetWarning(Notification notification, string url)
    {
        var threshold = ReadNumber(notification.Payload, "ThresholdPercentage");
        var body = threshold is null
            ? "Een waarschuwing voor het parkeerbudget is bereikt."
            : $"{threshold:0.#}% van het parkeerbudget is bereikt.";
        return new("Parkeerbudget waarschuwing", body, url, notification.Id);
    }

    private static string? ReadString(string? payload, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return TryGetProperty(document.RootElement, propertyName, out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static decimal? ReadNumber(string? payload, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return TryGetProperty(document.RootElement, propertyName, out var value) &&
                   value.TryGetDecimal(out var number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
