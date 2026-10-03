namespace Parkeren.Application.Visits;

public sealed class FailedSchedulerWorkReleaseQueue
{
    private readonly Dictionary<Guid, string> pending = [];

    public int Count => pending.Count;

    public void Enqueue(Guid workId, string workerId)
    {
        if (workId == Guid.Empty)
            throw new ArgumentException("Scheduler work id is required.", nameof(workId));
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker id is required.", nameof(workerId));

        pending[workId] = workerId;
    }

    public async Task RetryPendingAsync(
        IVisitSchedulerWorkClaimer claimer,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimer);

        foreach (var (workId, workerId) in pending.ToArray())
        {
            await claimer.ReleaseFailedAsync(workId, workerId, retryAt, cancellationToken);
            pending.Remove(workId);
        }
    }
}