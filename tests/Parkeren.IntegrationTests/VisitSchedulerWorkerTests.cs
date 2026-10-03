using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parkeren.Api;
using Parkeren.Application.Visits;
using Parkeren.Domain.Visits;

namespace Parkeren.IntegrationTests;

public sealed class VisitSchedulerWorkerTests
{
    [Fact]
    public async Task Worker_retries_failed_release_before_claiming_more_work_without_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var claimer = new FailReleaseTwiceClaimer();
        var processor = new ThrowingWorkProcessor();
        var releaseQueue = new FailedSchedulerWorkReleaseQueue();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IVisitRecoveryService, NoOpVisitRecoveryService>();
        services.AddSingleton<IVisitTerminalRecoveryService, NoOpVisitTerminalRecoveryService>();
        services.AddSingleton<IVisitSchedulerWorkClaimer>(claimer);
        services.AddSingleton<IVisitSchedulerWorkProcessor>(processor);

        await using var provider = services.BuildServiceProvider();
        var worker = new VisitSchedulerWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            releaseQueue,
            TimeProvider.System,
            provider.GetRequiredService<ILogger<VisitSchedulerWorker>>());

        try
        {
            await worker.StartAsync(cancellationToken);
            await claimer.SecondReleaseFailed.Task.WaitAsync(cancellationToken);

            Assert.Equal(1, claimer.ClaimCalls);
            Assert.Equal(1, processor.Calls);
            Assert.Equal(1, releaseQueue.Count);

            await claimer.ClaimAfterReleaseSucceeded.Task.WaitAsync(cancellationToken);

            Assert.Equal(3, claimer.ReleaseCalls);
            Assert.Equal(2, claimer.ClaimCalls);
            Assert.Equal(0, releaseQueue.Count);
            Assert.Equal(1, processor.Calls);
            Assert.All(claimer.ReleaseOwners, owner => Assert.Equal(claimer.ReleaseOwners[0], owner));
        }
        finally
        {
            await worker.StopAsync(cancellationToken);
            await provider.DisposeAsync();
        }
    }

    private sealed class FailReleaseTwiceClaimer : IVisitSchedulerWorkClaimer
    {
        private readonly VisitSchedulerWork work = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VisitSchedulerWorkType.ContinueProviderCoverage,
            DateTimeOffset.UtcNow);

        private int claimCalls;
        private int releaseCalls;

        public TaskCompletionSource SecondReleaseFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ClaimAfterReleaseSucceeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> ReleaseOwners { get; } = [];
        public int ClaimCalls => Volatile.Read(ref claimCalls);
        public int ReleaseCalls => Volatile.Read(ref releaseCalls);

        public Task<VisitSchedulerWork?> ClaimNextDueAsync(
            string workerId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref claimCalls);
            if (call == 1)
            {
                work.Claim(workerId, now);
                return Task.FromResult<VisitSchedulerWork?>(work);
            }

            if (ReleaseCalls >= 2)
                ClaimAfterReleaseSucceeded.TrySetResult();

            return Task.FromResult<VisitSchedulerWork?>(null);
        }

        public Task ReleaseFailedAsync(
            Guid workId,
            string workerId,
            DateTimeOffset retryAt,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(work.Id, workId);
            ReleaseOwners.Add(workerId);
            var call = Interlocked.Increment(ref releaseCalls);
            if (call <= 2)
            {
                if (call == 2)
                    SecondReleaseFailed.TrySetResult();

                return Task.FromException(new InvalidOperationException("Temporary release outage."));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingWorkProcessor : IVisitSchedulerWorkProcessor
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task ProcessAsync(VisitSchedulerWork work, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return Task.FromException(new InvalidOperationException("Transient work processing failure."));
        }
    }

    private sealed class NoOpVisitRecoveryService : IVisitRecoveryService
    {
        public Task<IReadOnlyList<VisitRecoveryItem>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VisitRecoveryItem>>([]);

        public Task RecoverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecoverExpiredInProgressOperationsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconcileActiveProviderActionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconcileUnknownOperationsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpVisitTerminalRecoveryService : IVisitTerminalRecoveryService
    {
        public Task RecoverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}