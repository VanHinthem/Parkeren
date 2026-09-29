using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class ProviderParkingActionExternalStopTests
{
    [Fact]
    public void External_stop_records_provider_fact_without_inventing_stop_time()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        var action = new ProviderParkingAction(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(4));
        action.MarkStarting();
        action.MarkActive("provider-123", start);

        action.MarkExternallyStopped("stopped");

        Assert.Equal(ProviderActionState.Stopped, action.State);
        Assert.Equal("stopped", action.ProviderStatus);
        Assert.Null(action.ActualEndAt);
    }
}
