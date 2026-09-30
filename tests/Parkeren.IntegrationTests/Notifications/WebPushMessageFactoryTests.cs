using System.Text.Json;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure.Notifications;

namespace Parkeren.IntegrationTests.Notifications;

public sealed class WebPushMessageFactoryTests
{
    [Fact]
    public void Visit_notification_links_to_visit()
    {
        var visitId = Guid.NewGuid();
        var notification = new Notification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.VisitStarted,
            DateTimeOffset.UtcNow,
            visitId);

        var message = WebPushMessageFactory.Create(notification);

        Assert.NotNull(message);
        Assert.Equal("Parkeren gestart", message.Title);
        Assert.Equal($"/acties/{visitId}", message.Url);
    }

    [Fact]
    public void Long_visit_uses_license_plate_from_payload()
    {
        var notification = new Notification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.LongVisitWarning,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            payload: JsonSerializer.Serialize(new { licensePlate = "12ABC3" }));

        var message = WebPushMessageFactory.Create(notification);

        Assert.NotNull(message);
        Assert.Contains("12ABC3", message.Body);
    }

    [Fact]
    public void Budget_warning_uses_threshold_from_payload()
    {
        var notification = new Notification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.BudgetWarning,
            DateTimeOffset.UtcNow,
            payload: JsonSerializer.Serialize(new { thresholdPercentage = 90 }));

        var message = WebPushMessageFactory.Create(notification);

        Assert.NotNull(message);
        Assert.Contains("90%", message.Body);
        Assert.Equal("/meldingen", message.Url);
    }

    [Fact]
    public void Serialize_uses_service_worker_payload_shape()
    {
        var json = WebPushMessageFactory.Serialize(new WebPushMessage("Titel", "Tekst", "/meldingen"));
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Titel", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Tekst", document.RootElement.GetProperty("body").GetString());
        Assert.Equal("/meldingen", document.RootElement.GetProperty("url").GetString());
    }
}
