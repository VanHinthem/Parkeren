using Parkeren.Domain.Notifications;

namespace Parkeren.Domain.Tests;

public sealed class PushDeliveryTests
{
    [Fact]
    public void New_delivery_is_pending()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-30T12:00:00Z");

        var delivery = new PushDelivery(Guid.NewGuid(), Guid.NewGuid(), createdAt);

        Assert.Equal(PushDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Null(delivery.LastAttemptAt);
        Assert.Null(delivery.DeliveredAt);
    }

    [Fact]
    public void MarkAttempt_tracks_attempt_count_and_time()
    {
        var delivery = new PushDelivery(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var first = DateTimeOffset.Parse("2026-09-30T12:01:00Z");
        var second = DateTimeOffset.Parse("2026-09-30T12:02:00Z");

        delivery.MarkAttempt(first);
        delivery.MarkAttempt(second);

        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(second, delivery.LastAttemptAt);
        Assert.Equal(PushDeliveryStatus.Pending, delivery.Status);
    }

    [Fact]
    public void MarkDelivered_completes_delivery()
    {
        var delivery = new PushDelivery(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var deliveredAt = DateTimeOffset.Parse("2026-09-30T12:03:00Z");

        delivery.MarkDelivered(deliveredAt);

        Assert.Equal(PushDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(deliveredAt, delivery.DeliveredAt);
    }

    [Fact]
    public void MarkFailed_marks_delivery_failed()
    {
        var delivery = new PushDelivery(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        delivery.MarkFailed();

        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
    }
}
