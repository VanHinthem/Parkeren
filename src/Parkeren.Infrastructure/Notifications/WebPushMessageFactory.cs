using System.Text.Json;
using Parkeren.Domain.Notifications;

namespace Parkeren.Infrastructure.Notifications;

public sealed record WebPushMessage(string Title, string Body, string Url);

public static class WebPushMessageFactory
{
    public static WebPushMessage? Create(Notification notification)
    {
        var url = notification.VisitId is Guid visitId
            ? $"/acties/{visitId}"
            : "/meldingen";

        return notification.Type switch
        {
            NotificationType.VisitStarted => new("Parkeren gestart", "De parkeeractie is gestart.", url),
            NotificationType.VisitStopped => new("Parkeren gestopt", "De parkeeractie is gestopt.", url),
            NotificationType.ProviderContinuationSucceeded => new("Parkeren voortgezet", "De parkeeractie is automatisch voortgezet.", url),
            NotificationType.ProviderContinuationAttentionRequired => new("Parkeren vraagt aandacht", "De parkeeractie kon niet automatisch worden voortgezet.", url),
            NotificationType.LongVisitWarning => CreateLongVisit(notification, url),
            NotificationType.BudgetWarning => CreateBudgetWarning(notification, url),
            _ => null
        };
    }

    public static string Serialize(WebPushMessage message) =>
        JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = message.Url
        });

    private static WebPushMessage CreateLongVisit(Notification notification, string url)
    {
        var licensePlate = ReadString(notification.Payload, "LicensePlate");
        var body = string.IsNullOrWhiteSpace(licensePlate)
            ? "Een parkeeractie duurt langer dan ingesteld."
            : $"Parkeeractie {licensePlate} duurt langer dan ingesteld.";
        return new("Langdurig parkeren", body, url);
    }

    private static WebPushMessage CreateBudgetWarning(Notification notification, string url)
    {
        var threshold = ReadNumber(notification.Payload, "ThresholdPercentage");
        var body = threshold is null
            ? "Een waarschuwing voor het parkeerbudget is bereikt."
            : $"{threshold:0.#}% van het parkeerbudget is bereikt.";
        return new("Parkeerbudget waarschuwing", body, url);
    }

    private static string? ReadString(string? payload, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? ReadNumber(string? payload, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
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
