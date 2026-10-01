using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderParkingActionMissingTests
{
    [Fact]
    public void Missing_provider_action_can_finish_stopping_without_inventing_end_time()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(4));
        action.MarkStarting();
        action.MarkActive("provider-missing", start, "active");
        action.BeginStopping();

        action.MarkProviderMissing();

        Assert.Equal(ProviderActionState.Stopped, action.State);
        Assert.Equal(ProviderActionHealth.Healthy, action.Health);
        Assert.Equal("missing", action.ProviderStatus);
        Assert.Null(action.ActualEndAt);
    }
}
