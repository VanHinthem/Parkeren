using System.Text.Json;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure.Notifications;

namespace Parkeren.IntegrationTests.Notifications;

public sealed class WebPushMessageFactoryTests
{
    [Fact]
    public void Visit_started_notification_links_to_dashboard()
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
        Assert.Equal("/", message.Url);
        Assert.Equal(notification.Id, message.NotificationId);
    }


    [Fact]
    public void Visit_stopped_notification_links_to_visit_detail()
    {
        var visitId = Guid.NewGuid();
        var notification = new Notification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.VisitStopped,
            DateTimeOffset.UtcNow,
            visitId);

        var message = WebPushMessageFactory.Create(notification);

        Assert.NotNull(message);
        Assert.Equal($"/acties/{visitId}", message.Url);
    }

    [Fact]
    public void Provider_continuation_succeeded_links_to_dashboard()
    {
        var notification = new Notification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationType.ProviderContinuationSucceeded,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        var message = WebPushMessageFactory.Create(notification);

        Assert.NotNull(message);
        Assert.Equal("/", message.Url);
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
        var notificationId = Guid.NewGuid();
        var json = WebPushMessageFactory.Serialize(new WebPushMessage("Titel", "Tekst", "/meldingen", notificationId));
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Titel", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Tekst", document.RootElement.GetProperty("body").GetString());
        Assert.Equal("/meldingen", document.RootElement.GetProperty("url").GetString());
        Assert.Equal(notificationId, document.RootElement.GetProperty("notificationId").GetGuid());
    }
}
