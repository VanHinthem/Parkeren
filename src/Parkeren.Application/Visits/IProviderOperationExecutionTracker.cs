using System.Collections.Concurrent;

namespace Parkeren.Application.Visits;

public interface IProviderOperationExecutionTracker
{
    IDisposable? TryTrack(Guid operationId);
    IDisposable? TryClaimRecovery(Guid operationId);
    bool IsActive(Guid operationId);
    Task WaitUntilInactiveAsync(Guid operationId, CancellationToken cancellationToken = default);
}

public sealed class ProviderOperationExecutionTracker : IProviderOperationExecutionTracker
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> activeOperations = new();

    public IDisposable? TryTrack(Guid operationId) => TryAcquire(operationId);

    public IDisposable? TryClaimRecovery(Guid operationId) => TryAcquire(operationId);

    public bool IsActive(Guid operationId) => activeOperations.ContainsKey(operationId);

    public async Task WaitUntilInactiveAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        while (activeOperations.TryGetValue(operationId, out var released))
            await released.Task.WaitAsync(cancellationToken);
    }

    private IDisposable? TryAcquire(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Provider operation id is required.", nameof(operationId));
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!activeOperations.TryAdd(operationId, released))
            return null;

        return new ExecutionLease(this, operationId);
    }

    private void Release(Guid operationId)
    {
        if (activeOperations.TryRemove(operationId, out var released))
            released.TrySetResult();
    }

    private sealed class ExecutionLease(ProviderOperationExecutionTracker tracker, Guid operationId) : IDisposable
    {
        private ProviderOperationExecutionTracker? owner = tracker;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(operationId);
    }
}