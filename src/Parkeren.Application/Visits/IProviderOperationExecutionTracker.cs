using System.Collections.Concurrent;

namespace Parkeren.Application.Visits;

public interface IProviderOperationExecutionTracker
{
    IDisposable? TryTrack(Guid operationId);
    IDisposable? TryClaimRecovery(Guid operationId);
    bool IsActive(Guid operationId);
}

public sealed class ProviderOperationExecutionTracker : IProviderOperationExecutionTracker
{
    private readonly ConcurrentDictionary<Guid, byte> activeOperations = new();

    public IDisposable? TryTrack(Guid operationId) => TryAcquire(operationId);

    public IDisposable? TryClaimRecovery(Guid operationId) => TryAcquire(operationId);

    public bool IsActive(Guid operationId) => activeOperations.ContainsKey(operationId);

    private IDisposable? TryAcquire(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Provider operation id is required.", nameof(operationId));
        if (!activeOperations.TryAdd(operationId, 0))
            return null;

        return new ExecutionLease(this, operationId);
    }

    private void Release(Guid operationId) => activeOperations.TryRemove(operationId, out _);

    private sealed class ExecutionLease(ProviderOperationExecutionTracker tracker, Guid operationId) : IDisposable
    {
        private ProviderOperationExecutionTracker? owner = tracker;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(operationId);
    }
}