using Parkeren.Domain.Visits;
using Xunit;

namespace Parkeren.Domain.Tests;
public sealed class ProviderOperationTests
{
    [Fact]
    public void Unknown_operation_must_reconcile_before_success()
    {
        var operationId = Guid.NewGuid();
        var operation = new ProviderOperation(Guid.NewGuid(), operationId, Guid.NewGuid(), null, ProviderOperationType.Start);
        operation.BeginAttempt(); operation.MarkUnknown("timeout");
        Assert.Equal(operationId, operation.OperationId);
        Assert.Throws<InvalidOperationException>(() => operation.BeginAttempt());
        operation.BeginReconciliation(); operation.Succeed(DateTimeOffset.UtcNow);
        Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
    }
}
