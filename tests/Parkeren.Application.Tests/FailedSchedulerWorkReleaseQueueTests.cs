using Parkeren.Application.Visits;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class FailedSchedulerWorkReleaseQueueTests
{
    [Fact]
    public async Task Failed_release_remains_queued_until_owner_retry_succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var queue = new FailedSchedulerWorkReleaseQueue();
        var workId = Guid.NewGuid();
        queue.Enqueue(workId, "worker-owner");
        var claimer = new FailOnceClaimer();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.RetryPendingAsync(claimer, DateTimeOffset.UtcNow, cancellationToken));

        Assert.Equal(1, queue.Count);
        Assert.Equal(1, claimer.Calls);

        await queue.RetryPendingAsync(claimer, DateTimeOffset.UtcNow, cancellationToken);

        Assert.Equal(0, queue.Count);
        Assert.Equal(2, claimer.Calls);
        Assert.All(claimer.ReleasedWork, release =>
        {
            Assert.Equal(workId, release.WorkId);
            Assert.Equal("worker-owner", release.WorkerId);
        });
    }

    private sealed class FailOnceClaimer : IVisitSchedulerWorkClaimer
    {
        public int Calls { get; private set; }
        public List<(Guid WorkId, string WorkerId)> ReleasedWork { get; } = [];

        public Task<Parkeren.Domain.Visits.VisitSchedulerWork?> ClaimNextDueAsync(
            string workerId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Parkeren.Domain.Visits.VisitSchedulerWork?>(null);

        public Task ReleaseFailedAsync(
            Guid workId,
            string workerId,
            DateTimeOffset retryAt,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ReleasedWork.Add((workId, workerId));
            return Calls == 1
                ? Task.FromException(new InvalidOperationException("Transient persistence failure."))
                : Task.CompletedTask;
        }
    }
}