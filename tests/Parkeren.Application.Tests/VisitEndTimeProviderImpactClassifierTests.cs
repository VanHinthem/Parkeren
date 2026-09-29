using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class VisitEndTimeProviderImpactClassifierTests
{
    [Fact]
    public void No_provider_actions_requires_no_provider_mutation()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(1);

        var impact = VisitEndTimeProviderImpactClassifier.Classify(
            requestedEndAt,
            Array.Empty<ProviderParkingAction>());

        Assert.Equal(VisitEndTimeProviderImpact.None, impact);
    }

    [Fact]
    public void Active_action_ending_exactly_at_requested_end_requires_no_provider_mutation()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(1);
        var action = CreateActiveAction(requestedEndAt);

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, new[] { action });

        Assert.Equal(VisitEndTimeProviderImpact.None, impact);
    }

    [Fact]
    public void Active_action_extending_beyond_requested_end_requires_provider_mutation()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(1);
        var action = CreateActiveAction(requestedEndAt.AddMinutes(1));

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, new[] { action });

        Assert.Equal(VisitEndTimeProviderImpact.ShortenActive, impact);
    }

    [Fact]
    public void Stopped_action_extending_beyond_requested_end_requires_no_provider_mutation()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(1);
        var action = CreateActiveAction(requestedEndAt.AddMinutes(1));
        action.BeginStopping();
        action.MarkStopped(requestedEndAt.AddMinutes(-1), "stopped");

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, new[] { action });

        Assert.Equal(VisitEndTimeProviderImpact.None, impact);
    }


    [Fact]
    public void Scheduled_action_starting_at_requested_end_is_cancelled()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(1);
        var action = CreateScheduledAction(requestedEndAt, requestedEndAt.AddHours(1));

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, new[] { action });

        Assert.Equal(VisitEndTimeProviderImpact.CancelScheduled, impact);
    }

    [Fact]
    public void Scheduled_action_overlapping_requested_end_is_replaced()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(1);
        var action = CreateScheduledAction(requestedEndAt.AddMinutes(-30), requestedEndAt.AddMinutes(30));

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, new[] { action });

        Assert.Equal(VisitEndTimeProviderImpact.ReplaceScheduled, impact);
    }

    [Fact]
    public void Scheduled_action_within_requested_end_requires_no_provider_mutation()
    {
        var requestedEndAt = DateTimeOffset.UtcNow.AddHours(2);
        var action = CreateScheduledAction(requestedEndAt.AddHours(-1), requestedEndAt);

        var impact = VisitEndTimeProviderImpactClassifier.Classify(requestedEndAt, new[] { action });

        Assert.Equal(VisitEndTimeProviderImpact.None, impact);
    }

    private static ProviderParkingAction CreateScheduledAction(DateTimeOffset plannedStartAt, DateTimeOffset plannedEndAt)
    {
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), plannedStartAt, plannedEndAt);
        action.MarkStarting();
        action.MarkScheduled("provider-scheduled", "scheduled");
        return action;
    }

    private static ProviderParkingAction CreateActiveAction(DateTimeOffset plannedEndAt)
    {
        var action = new ProviderParkingAction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            plannedEndAt.AddHours(-1),
            plannedEndAt);
        action.MarkStarting();
        action.MarkActive("provider-action", plannedEndAt.AddHours(-1), "active");
        return action;
    }
}
