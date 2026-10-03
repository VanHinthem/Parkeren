using System.Threading;
using Parkeren.Application.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class ProviderOperationExecutionTrackerTests
{
    [Fact]
    public async Task Execution_and_recovery_claims_are_mutually_exclusive_when_racing()
    {
        var tracker = new ProviderOperationExecutionTracker();
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var start = new Barrier(3);

        var executionClaim = Task.Run(() =>
        {
            start.SignalAndWait(cancellationToken);
            return tracker.TryTrack(operationId);
        }, cancellationToken);
        var recoveryClaim = Task.Run(() =>
        {
            start.SignalAndWait(cancellationToken);
            return tracker.TryClaimRecovery(operationId);
        }, cancellationToken);

        start.SignalAndWait(cancellationToken);
        var claims = await Task.WhenAll(executionClaim, recoveryClaim);

        Assert.Single(claims, claim => claim is not null);
        Assert.True(tracker.IsActive(operationId));

        foreach (var claim in claims)
            claim?.Dispose();

        Assert.False(tracker.IsActive(operationId));
    }
}